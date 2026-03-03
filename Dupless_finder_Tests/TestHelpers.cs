using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dupples_finder_UI.DTO;
using OpenCvSharp;
using Prism.Commands;

namespace Dupless_finder_Tests;

/// <summary>
/// A SynchronizationContext that executes Post/Send callbacks inline (synchronously).
/// Sets itself as <see cref="SynchronizationContext.Current"/> before invoking the
/// callback, mirroring how real WPF DispatcherSynchronizationContext behaves.
/// Without this, continuations after <c>await Task.Run(...)</c> would execute on
/// a thread-pool thread where Current is null, causing <see cref="Progress{T}"/>
/// to fall back to <c>ThreadPool.QueueUserWorkItem</c> instead of posting inline.
/// </summary>
internal sealed class SynchronousSyncContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object state)
    {
        var prev = Current;
        SetSynchronizationContext(this);
        try { d(state); }
        finally { SetSynchronizationContext(prev); }
    }

    public override void Send(SendOrPostCallback d, object state)
    {
        var prev = Current;
        SetSynchronizationContext(this);
        try { d(state); }
        finally { SetSynchronizationContext(prev); }
    }
}

/// <summary>
/// Shared test helper methods used across MainViewModel test files.
/// Centralises reflection-based command invocation and leak-free PairSimilarityInfo
/// construction so that duplicates are eliminated.
/// </summary>
internal static class TestHelpers
{
    // Cached FieldInfo for extracting the async delegate from AsyncDelegateCommand.
    internal static readonly FieldInfo ExecuteMethodField =
        typeof(AsyncDelegateCommand)
            .GetField("_executeMethod", BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>
    /// Extracts the private _executeMethod delegate from an AsyncDelegateCommand
    /// via reflection and awaits the returned Task.
    /// Prism 9's AsyncDelegateCommand does not expose a public ExecuteAsync().
    /// </summary>
    internal static async Task InvokeCommandAsync(AsyncDelegateCommand command, object parameter = null)
    {
        if (ExecuteMethodField == null)
        {
            throw new InvalidOperationException(
                "Could not locate _executeMethod on AsyncDelegateCommand. " +
                "The Prism version may have changed.");
        }

        var executeDelegate = ExecuteMethodField.GetValue(command);

        // Prism 9 stores the delegate as Func<CancellationToken, Task> internally.
        if (executeDelegate is Func<CancellationToken, Task> funcCt)
        {
            await funcCt(CancellationToken.None);
            return;
        }

        // Prism 8 / other overloads.
        if (executeDelegate is Func<object, Task> funcObjTask)
        {
            await funcObjTask(parameter);
            return;
        }

        if (executeDelegate is Func<Task> funcTask)
        {
            await funcTask();
            return;
        }

        throw new InvalidOperationException(
            $"_executeMethod has unexpected type: {executeDelegate?.GetType().FullName}");
    }

    /// <summary>
    /// Shared Mat instances for BuildPairSimilarityInfo. These are tiny 1x1 Mats
    /// that serve as lightweight placeholders. Static readonly — allocated once,
    /// never disposed (acceptable for small test-only objects).
    /// PairSimilarityInfo.Equals only compares Key strings, not Mat values,
    /// so sharing Mats is safe.
    /// </summary>
    private static readonly Mat SharedMat1 = Mat.Zeros(1, 1, MatType.CV_8UC3);
    private static readonly Mat SharedMat2 = Mat.Zeros(1, 1, MatType.CV_8UC3);

    /// <summary>
    /// Creates a PairSimilarityInfo using shared static Mat instances,
    /// avoiding per-call Mat allocations that would leak native memory.
    /// </summary>
    internal static PairSimilarityInfo BuildPairSimilarityInfo(
        string path1,
        string path2,
        double score)
    {
        var kv1 = new KeyValuePair<string, Mat>(path1, SharedMat1);
        var kv2 = new KeyValuePair<string, Mat>(path2, SharedMat2);
        return new PairSimilarityInfo(kv1, kv2, score);
    }
}