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

namespace Dupples_finder_UI.DTO
{
    /// <summary>
    /// Represents an image file with its metadata, thumbnail, and SIFT data.
    /// The constructor immediately reads FileInfo for size/lastModified (fingerprint).
    /// Thumbnails are loaded asynchronously via DB cache or OS Shell API.
    /// StoredMat is loaded on-demand only when SIFT hashing is needed.
    /// </summary>
    public class ImageInfo : DisposableObject, INotifyPropertyChanged
    {
        /// <summary>Semaphore for controlled HDD load during StoredMat loading.</summary>
        private static readonly Semaphore Sem = new(Environment.ProcessorCount, Environment.ProcessorCount);

        /// <summary>Lock for thread-safe StoredMat access.</summary>
        private static readonly object Lock = new();

        private readonly IEventAggregator _eventAggregator;

        public string FilePath { get; set; }

        public string FileName => Path.GetFileName(FilePath);

        /// <summary>File size in bytes, read from FileInfo at construction time.</summary>
        public long FileSize { get; private set; }

        /// <summary>File's last-modified timestamp (UTC), read from FileInfo at construction time.</summary>
        public DateTime LastModifiedUtc { get; private set; }

        /// <summary>
        /// Lightweight identity fingerprint combining FileSize and LastModifiedUtc.
        /// Matches the Photo entity's Fingerprint property format.
        /// </summary>
        public string Fingerprint => $"{FileSize}_{LastModifiedUtc.Ticks}";

        private const double StoredMatDecodeSize = 200;

        private Mat _storedMat;
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

        private ImageSource _image;
        /// <summary>
        /// The thumbnail image for UI display. Initially null (placeholder),
        /// then populated asynchronously via LoadThumbnailAsync.
        /// </summary>
        public ImageSource Image
        {
            get => _image;
            private set
            {
                _image = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Indicates whether the thumbnail has been loaded yet.
        /// </summary>
        public bool IsThumbnailLoaded => _image != null;

        public ImageInfo(string path, IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
            FilePath = path;

            // Immediately read file metadata for fingerprint
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

        /// <summary>
        /// Loads the thumbnail using the DB-first, Shell-fallback pipeline.
        /// 1. Check DB for cached thumbnail via fingerprint
        /// 2. If not in DB, generate via IThumbnailService.GetThumbnail() (OS Shell API)
        /// 3. Store generated thumbnail to DB via IPhotoDbService.CacheThumbnailAsync()
        ///
        /// This method is designed to be called from a background thread.
        /// It dispatches the final Image assignment to the UI thread.
        /// </summary>
        public void LoadThumbnail(IPhotoDbService dbService, IThumbnailService thumbnailService)
        {
            try
            {
                BitmapSource thumbnail = null;

                // Step 1: Try DB cache
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

                // Step 2: Generate via OS Shell API if not cached
                if (thumbnail == null)
                {
                    thumbnail = thumbnailService.GetThumbnail(FilePath);

                    // Step 3: Store to DB for next time -- encode bytes from the
                    // BitmapSource we already have instead of making a second Shell call.
                    if (thumbnail != null && dbService is { IsAvailable: true })
                    {
                        try
                        {
                            byte[] thumbBytes = thumbnailService.EncodeBitmapSourceToBytes(thumbnail);
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

                // Step 4: Fall back to OpenCvSharp if Shell API fails
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

                    // Update on UI thread
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

        /// <summary>The command that fires during doubleClick on thumbnail.</summary>
        public DelegateCommand ImageDoubleClick { get; private set; }
        public DelegateCommand ImageClick { get; private set; }

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

        /// <summary>
        /// Loads the full image as a resized Mat for SIFT computation.
        /// Only called when StoredMat is accessed for hashing.
        /// Thread-safe: uses double-check locking to prevent redundant loads.
        /// </summary>
        public void StoreMat(double decodeSize)
        {
            if (_storedMat != null)
            {
                return;
            }

            lock (Lock)
            {
                // Double-check inside lock to prevent duplicate work
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

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region disposing

        protected override void Clean()
        {
            lock (Lock)
            {
                _storedMat?.Release();
                _storedMat = null;
            }
            _image = null;
        }

        #endregion
    }
}
