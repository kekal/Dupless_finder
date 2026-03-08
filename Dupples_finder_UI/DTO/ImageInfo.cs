using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dupples_finder_UI.Events;
using Dupples_finder_UI.Services;
using Dupples_finder_UI.Services.Interfaces;
using OpenCvSharp;
using Prism.Commands;
using Prism.Events;
using DisposableObject = Dupples_finder_UI.Modules.Helpers.DisposableObject;
using Size = OpenCvSharp.Size;

namespace Dupples_finder_UI.DTO;

/// <summary>
/// Represents an image file with its metadata, thumbnail, and SIFT data.
/// The constructor immediately reads FileInfo for size/lastModified (fingerprint).
/// Thumbnails are loaded asynchronously via DB cache or OS Shell API.
/// StoredMat is loaded on-demand only when SIFT hashing is needed.
/// </summary>
public class ImageInfo : DisposableObject, INotifyPropertyChanged
{
    private const double StoredMatDecodeSize = 200;

    private readonly IEventAggregator _eventAggregator;

    private ImageSource _image;

    private Mat _storedMat;

    /// <summary>Lock for thread-safe StoredMat access.</summary>
    private static readonly object Lock = new();

    /// <summary>Semaphore for controlled HDD load during StoredMat loading.</summary>
    private static readonly Semaphore Sem = new(Environment.ProcessorCount, Environment.ProcessorCount);

    public ImageInfo(string path, IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator;
        FilePath = path;

        try
        {
            var fileInfo = new FileInfo(path);

            FileSize = fileInfo.Length;
            LastModifiedUtc = fileInfo.LastWriteTimeUtc;
        }
        catch (Exception ex)
        {
            PerfLogger.Log($"Failed to read FileInfo for '{path}': {ex.Message}");
        }

        DefineCommands();
    }

    /// <summary>
    /// Fast constructor that uses pre-fetched FileInfo metadata.
    /// Avoids per-file stat calls on network shares where DirectoryInfo.EnumerateFiles()
    /// already populated the FileInfo from the directory listing.
    /// </summary>
    public ImageInfo(FileInfo fileInfo, IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator;
        FilePath = fileInfo.FullName;

        try
        {
            FileSize = fileInfo.Length;
            LastModifiedUtc = fileInfo.LastWriteTimeUtc;
        }
        catch (Exception ex)
        {
            PerfLogger.Log($"Failed to read FileInfo for '{fileInfo.FullName}': {ex.Message}");
        }

        DefineCommands();
    }

    public event PropertyChangedEventHandler PropertyChanged;

    public string FileName => Path.GetFileName(FilePath);

    public string FilePath { get; set; }

    public long FileSize { get; }

    /// <summary>
    /// Lightweight identity fingerprint combining FileSize and LastModifiedUtc.
    /// Matches the Photo entity's Fingerprint property format.
    /// </summary>
    public string Fingerprint => $"{FileSize}_{LastModifiedUtc.Ticks}";

    public ImageSource Image
    {
        get => _image;
        private set
        {
            if (ReferenceEquals(_image, value))
            {
                return;
            }

            _image = value;
            OnPropertyChanged();
        }
    }

    public DelegateCommand ImageClick { get; private set; }

    public DelegateCommand ImageDoubleClick { get; private set; }

    public bool IsThumbnailLoaded => _image != null;

    public DateTime LastModifiedUtc { get; }

    /// <summary>
    /// OpenCv Mat for SIFT computation. Loaded on-demand from the full image file.
    /// This is NOT used for thumbnails -- thumbnails come from ThumbnailService.
    /// </summary>
    public Mat StoredMat
    {
        get
        {
            if (_storedMat == null)
            {
                StoreMat(StoredMatDecodeSize);
            }
            return _storedMat;
        }
    }

    /// <summary>
    /// Loads the thumbnail using the DB-first, Shell-fallback pipeline and sets <see cref="Image"/>.
    /// The thumbnail is frozen for cross-thread safety; the caller must invoke this
    /// so that the continuation (Image setter) runs on the UI thread.
    /// </summary>
    public async Task LoadThumbnailAsync(IPhotoDbService dbService, IThumbnailService thumbnailService)
    {
        using var op = PerfLogger.TimedVerbose("THUMB");
        var source = "NONE";

        try
        {
            BitmapSource thumbnail = null;
            var fromCache = false;

            // ── Step 1: In-memory pre-loaded cache ──
            if (dbService is { IsAvailable: true } &&
                dbService.TryGetCachedThumbnail(FileSize, LastModifiedUtc, out var cachedBytes))
            {
                thumbnail = thumbnailService.BytesToBitmapSource(cachedBytes);
                fromCache = thumbnail != null;
                if (fromCache)
                {
                    source = "MEM_CACHE";
                }
            }
            op.Lap("memCache");

            // ── Step 2: DB query fallback (skipped when memory cache is preloaded) ──
            if (thumbnail == null && dbService is { IsAvailable: true } && !dbService.IsCachePreloaded)
            {
                try
                {
                    var cached = await dbService.GetCachedPhotoByFingerprintAsync(FileSize, LastModifiedUtc);
                    if (cached?.Thumbnail is { Length: > 0 })
                    {
                        thumbnail = thumbnailService.BytesToBitmapSource(cached.Thumbnail);
                        fromCache = thumbnail != null;
                        if (fromCache)
                        {
                            source = "DB_CACHE";
                        }
                    }
                }
                catch (Exception ex)
                {
                    PerfLogger.Log($"DB thumbnail lookup failed for '{FileName}': {ex.Message}");
                }
            }
            op.Lap("db");

            // ── Step 3: EXIF embedded thumbnail (raw byte parsing, fully parallel) ──
            if (thumbnail == null)
            {
                var exifBytes = thumbnailService.GetEmbeddedThumbnailBytes(FilePath);
                if (exifBytes != null)
                {
                    thumbnail = thumbnailService.BytesToBitmapSource(exifBytes);
                    if (thumbnail != null)
                    {
                        source = "EXIF";
                        // Queue raw EXIF bytes for DB cache (skip re-encoding)
                        if (!fromCache && dbService is { IsAvailable: true })
                        {
                            dbService.QueueThumbnailForCache(FileSize, LastModifiedUtc, FilePath, exifBytes);
                            fromCache = true;
                        }
                    }
                }
            }
            op.Lap("exif");

            // ── Step 4: Shell fallback (COM STA, serialized — slower) ──
            if (thumbnail == null)
            {
                thumbnail = thumbnailService.GetThumbnail(FilePath);
                if (thumbnail != null)
                {
                    source = "SHELL";
                }
            }
            op.Lap("shell");

            // ── Step 5: OpenCvSharp full decode ──
            if (thumbnail == null)
            {
                Sem.WaitOne();
                try
                {
                    StoreMat(StoredMatDecodeSize);
                    if (_storedMat != null && !_storedMat.Empty())
                    {
                        thumbnail = OpenCvSharp.WpfExtensions.BitmapSourceConverter.ToBitmapSource(_storedMat);
                        source = "OPENCV";
                    }
                }
                finally
                {
                    Sem.Release();
                }
            }
            op.Lap("opencv");

            // ── Step 6: Encode & queue for DB ──
            if (thumbnail != null && !fromCache && dbService is { IsAvailable: true })
            {
                try
                {
                    var thumbBytes = thumbnailService.EncodeBitmapSourceToBytes(thumbnail);
                    if (thumbBytes is { Length: > 0 })
                    {
                        dbService.QueueThumbnailForCache(FileSize, LastModifiedUtc, FilePath, thumbBytes);
                    }
                }
                catch (Exception ex)
                {
                    PerfLogger.Log($"DB thumbnail store failed for '{FileName}': {ex.Message}");
                }
            }
            op.Lap("encode");

            if (thumbnail is { IsFrozen: false })
            {
                thumbnail.Freeze();
            }

            Image = thumbnail;

            var idx = Interlocked.Increment(ref _globalThumbIndex);
            op.Detail($"#{idx}");
            op.Detail($"source={source}");
            op.Detail($"file={FilePath}");
            op.Detail($"fileSize={FileSize}");
            op.Detail($"thumbPx={(thumbnail != null ? $"{thumbnail.PixelWidth}x{thumbnail.PixelHeight}" : "null")}");

            // Log every image if slow (>200ms), otherwise sample every 100th
            if (!(op.ElapsedMs > 200 || idx % 100 == 0))
            {
                op.Suppress();
            }
        }
        catch (Exception ex)
        {
            op.Suppress();
            PerfLogger.Log($"[PERF] THUMB FAILED in {op.ElapsedMs}ms | file={FilePath} | error={ex.Message} | thread={Environment.CurrentManagedThreadId}");
        }
    }

    private static int _globalThumbIndex;

    public void StoreMat(double decodeSize)
    {
        if (_storedMat != null)
        {
            return;
        }

        lock (Lock)
        {
            if (_storedMat != null)
            {
                return;
            }

            Mat sourceMat = null;
            try
            {
                sourceMat = new Mat(FilePath);

                if (sourceMat.Width < 1 || sourceMat.Height < 1)
                {
                    _storedMat = Mat.Zeros(new Size(decodeSize, decodeSize), MatType.CV_8UC3);
                    return;
                }

                var scale = Math.Min(decodeSize / sourceMat.Width, decodeSize / sourceMat.Height);
                _storedMat = sourceMat.Resize(new Size(0, 0), scale, scale, InterpolationFlags.Area);
            }
            catch (Exception ex)
            {
                PerfLogger.Log($"StoreMat failed for '{FileName}': {ex.Message}");
                _storedMat = Mat.Zeros(new Size(decodeSize, decodeSize), MatType.CV_8UC3);
            }
            finally
            {
                sourceMat?.Release();
            }
        }
    }

    protected override void Clean()
    {
        lock (Lock)
        {
            _storedMat?.Release();
            _storedMat = null;
        }
        _image = null;
    }

    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void DefineCommands()
    {
        ImageDoubleClick = new DelegateCommand(() =>
        {
            _eventAggregator?.GetEvent<OpenImagePreviewEvent>()
                .Publish(new OpenImagePreviewPayload { FilePath = FilePath });
        });

        ImageClick = new DelegateCommand(() =>
        {
            try
            {
                var psi = new ProcessStartInfo(FilePath) { UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
            }
        });
    }
}