using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dupples_finder_UI.Events;
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
            if (fileInfo.Exists)
            {
                FileSize = fileInfo.Length;
                LastModifiedUtc = fileInfo.LastWriteTimeUtc;
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to read FileInfo for '{path}': {ex.Message}");
        }

        PrepareCommands();
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
    /// Loads the thumbnail using the DB-first, Shell-fallback pipeline.
    /// 1. Check DB for cached thumbnail
    /// 2. If not in DB, generate
    /// 3. Store generated thumbnail to DB
    /// </summary>
    public void LoadThumbnail(IPhotoDbService dbService, IThumbnailService thumbnailService)
    {
        try
        {
            BitmapSource thumbnail = null;

            if (dbService is { IsAvailable: true })
            {
                try
                {
                    var cached = dbService.GetCachedPhotoByFingerprintAsync(FileSize, LastModifiedUtc)
                        .GetAwaiter().GetResult();
                    if (cached?.Thumbnail is { Length: > 0 })
                    {
                        thumbnail = thumbnailService.BytesToBitmapSource(cached.Thumbnail);
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"DB thumbnail lookup failed for '{FileName}': {ex.Message}");
                }
            }

            if (thumbnail == null)
            {
                thumbnail = thumbnailService.GetThumbnail(FilePath);

                if (thumbnail != null && dbService is { IsAvailable: true })
                {
                    try
                    {
                        var thumbBytes = thumbnailService.EncodeBitmapSourceToBytes(thumbnail);
                        if (thumbBytes is { Length: > 0 })
                        {
                            dbService.CacheThumbnailAsync(FileSize, LastModifiedUtc, FilePath, thumbBytes)
                                .GetAwaiter().GetResult();
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"DB thumbnail store failed for '{FileName}': {ex.Message}");
                    }
                }
            }

            if (thumbnail == null)
            {
                Trace.WriteLine($"Shell thumbnail failed for '{FileName}', falling back to OpenCvSharp");
                Sem.WaitOne();
                try
                {
                    StoreMat(StoredMatDecodeSize);
                    if (_storedMat != null && !_storedMat.Empty())
                    {
                        thumbnail = OpenCvSharp.WpfExtensions.BitmapSourceConverter.ToBitmapSource(_storedMat);
                        thumbnail.Freeze();
                    }
                }
                finally
                {
                    Sem.Release();
                }
            }

            if (thumbnail != null)
            {
                // Ensure it's frozen for cross-thread access
                if (!thumbnail.IsFrozen)
                {
                    thumbnail.Freeze();
                }

                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    Image = thumbnail;
                }));
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"LoadThumbnail failed for '{FileName}': {ex.Message}");
        }
    }

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
                Trace.WriteLine($"StoreMat failed for '{FileName}': {ex.Message}");
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

    private void PrepareCommands()
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