using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Dupples_finder_UI.Services;

/// <summary>
/// Fire-and-forget performance logger that writes to a file on a dedicated
/// background thread.  The public <see cref="Log"/> method is non-blocking
/// and lock-free (Channel.TryWrite) so it adds near-zero overhead to the
/// calling thread — no I/O, no synchronisation, no UI impact.
///
/// <para>Log file: <c>%LOCALAPPDATA%\DuplessFinder\perf.log</c></para>
/// <para>The file is truncated on every application start so only the
/// current session is kept.</para>
/// </summary>
public static class PerfLogger
{
    // Unbounded channel — TryWrite never fails, never blocks the caller.
    private static readonly Channel<string> Queue =
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,   // one consumer thread
            SingleWriter = false   // many producers
        });

    private static readonly string LogPath;
    private static int _started;

    /// <summary>
    /// When true, detailed [PERF] diagnostic logs are written.
    /// Set the <c>DUPLESS_VERBOSE</c> environment variable to any non-empty value to enable.
    /// Error and lifecycle logs are always written regardless of this setting.
    /// </summary>
    public static bool IsVerbose { get; } =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DUPLESS_VERBOSE"));

    static PerfLogger()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DuplessFinder");
        Directory.CreateDirectory(dir);
        LogPath = Path.Combine(dir, "perf.log");

        // Truncate log file at startup so only the current session is kept
        try { File.WriteAllText(LogPath, $"=== Dupless Finder perf log — {DateTime.Now:yyyy-MM-dd HH:mm:ss} === verbose={IsVerbose}{Environment.NewLine}"); }
        catch { /* best-effort */ }

        EnsureConsumerStarted();
    }

    /// <summary>
    /// Enqueue a log line.  This is non-blocking and lock-free.
    /// Always writes regardless of <see cref="IsVerbose"/>.
    /// Use for errors, warnings, and key lifecycle events.
    /// </summary>
    public static void Log(string message)
    {
        // Prefix with high-resolution timestamp
        var ts = DateTime.Now.ToString("HH:mm:ss.fff");
        Queue.Writer.TryWrite($"[{ts}] {message}");
    }

    /// <summary>
    /// Enqueue a diagnostic log line.  Only writes when <see cref="IsVerbose"/> is true
    /// (i.e. the <c>DUPLESS_VERBOSE</c> environment variable is set).
    /// Use for detailed [PERF] instrumentation, per-image timings, etc.
    /// </summary>
    public static void Verbose(string message)
    {
        if (!IsVerbose)
        {
            return;
        }

        var ts = DateTime.Now.ToString("HH:mm:ss.fff");
        Queue.Writer.TryWrite($"[{ts}] {message}");
    }

    /// <summary>
    /// Start a timed scope that always logs on completion.
    /// Use for errors, warnings, and key lifecycle events.
    /// </summary>
    public static TimedOperation Timed(string name, long minMs = 0)
        => new(name, minMs, verbose: false);

    /// <summary>
    /// Start a timed scope that only logs when <see cref="IsVerbose"/> is true.
    /// Use for detailed [PERF] instrumentation.
    /// <code>
    /// // Simple:
    /// using var _ = PerfLogger.TimedVerbose("SCAN");
    /// var files = await ScanAsync();
    ///
    /// // With laps and details:
    /// using var op = PerfLogger.TimedVerbose("THUMB");
    /// DoStep1();  op.Lap("read");
    /// DoStep2();  op.Lap("parse");
    /// op.Detail($"file={path}");
    /// // → [PERF] THUMB in 15ms | read=5ms | parse=10ms | file=photo.jpg
    /// </code>
    /// </summary>
    public static TimedOperation TimedVerbose(string name, long minMs = 0)
        => new(name, minMs, verbose: true);

    /// <summary>
    /// Flush all pending messages and close the writer.
    /// Call once during app shutdown if you want to guarantee nothing is lost.
    /// </summary>
    public static async Task FlushAsync()
    {
        Queue.Writer.TryComplete();
        // Wait up to 5 s for the consumer to drain
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await Queue.Reader.Completion.WaitAsync(cts.Token); }
        catch { /* timeout — best effort */ }
    }

    private static void EnsureConsumerStarted()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            return;
        }

        // Long-running background thread — not a threadpool thread,
        // so it never interferes with the app's task scheduler.
        var thread = new Thread(ConsumeLoop)
        {
            IsBackground = true,
            Name = "PerfLogger-Writer",
            Priority = ThreadPriority.BelowNormal
        };
        thread.Start();
    }

    private static void ConsumeLoop()
    {
        try
        {
            using var writer = new StreamWriter(LogPath, append: true)
            {
                AutoFlush = false
            };

            var reader = Queue.Reader;
            var pending = 0;

            // Block on the channel — this thread sleeps when there is nothing to write.
            while (reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                while (reader.TryRead(out var line))
                {
                    writer.WriteLine(line);
                    pending++;

                    // Flush in batches of 64 to minimise disk I/O
                    if (pending >= 64)
                    {
                        writer.Flush();
                        pending = 0;
                    }
                }

                // Flush any remaining after draining the current burst
                if (pending > 0)
                {
                    writer.Flush();
                    pending = 0;
                }
            }

            // Channel completed — drain any stragglers
            while (reader.TryRead(out var line))
            {
                writer.WriteLine(line);
            }
            writer.Flush();
        }
        catch
        {
            // Logger must never crash the app
        }
    }

    /// <summary>
    /// A disposable scope that measures elapsed time and logs on completion.
    /// Created via <see cref="PerfLogger.Timed"/> or <see cref="PerfLogger.TimedVerbose"/>.
    /// <para>Supports split timing via <see cref="Lap"/> and free-form context via <see cref="Detail"/>.</para>
    /// </summary>
    public sealed class TimedOperation : IDisposable
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly string _name;
        private readonly bool _verbose;
        private readonly long _minMs;
        private long _lastLapMs;
        private List<(string name, long ms)> _laps;
        private List<string> _details;
        private bool _suppressed;

        internal TimedOperation(string name, long minMs, bool verbose)
        {
            _name = name;
            _minMs = minMs;
            _verbose = verbose;
        }

        /// <summary>Current elapsed milliseconds (live, does not stop the timer).</summary>
        public long ElapsedMs => _sw.ElapsedMilliseconds;

        /// <summary>
        /// Record a named split time. Returns delta ms since the previous Lap (or start).
        /// Only laps that are recorded appear in the final log output.
        /// </summary>
        public long Lap(string stepName)
        {
            var now = _sw.ElapsedMilliseconds;
            var delta = now - _lastLapMs;
            _lastLapMs = now;
            (_laps ??= new List<(string, long)>()).Add((stepName, delta));
            return delta;
        }

        /// <summary>Append free-form context (e.g. "files=42") to the final log line.</summary>
        public void Detail(string info)
        {
            (_details ??= new List<string>()).Add(info);
        }

        /// <summary>Suppress the automatic log on Dispose (use when the caller logs manually).</summary>
        public void Suppress() => _suppressed = true;

        public void Dispose()
        {
            _sw.Stop();
            if (_suppressed)
            {
                return;
            }

            if (_sw.ElapsedMilliseconds < _minMs)
            {
                return;
            }

            var sb = new StringBuilder();
            sb.Append("[PERF] ").Append(_name).Append(" in ").Append(_sw.ElapsedMilliseconds).Append("ms");

            if (_laps != null)
            {
                foreach (var (name, ms) in _laps)
                {
                    sb.Append(" | ").Append(name).Append('=').Append(ms).Append("ms");
                }
            }

            if (_details != null)
            {
                foreach (var d in _details)
                {
                    sb.Append(" | ").Append(d);
                }
            }

            if (_verbose)
            {
                Verbose(sb.ToString());
            }
            else
            {
                Log(sb.ToString());
            }
        }
    }
}
