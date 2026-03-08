using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dupples_finder_UI.DTO;
using Dupples_finder_UI.Events;
using Dupples_finder_UI.Modules.Helpers;
using Dupples_finder_UI.Services;
using Dupples_finder_UI.Services.Interfaces;
using OpenCvSharp;
using Prism.Commands;
using Prism.Events;
using Timer = System.Threading.Timer;

namespace Dupples_finder_UI.Modules.ViewModels;

public class MainViewModel : ViewModelBase
{
    private bool _isLoaded;

    private double _calcProgress;

    private double _previewSize;
    private readonly ICalcOperations _calcOperations;


    private IEnumerable<PairSimilarityInfo> _matches;

    // Keep a flat list for SIFT hashing (same items as ImageCollection)
    private IList<ImageInfo> _dataCollectionFlat = new List<ImageInfo>();

    private IList<ImagePair> _pairDataCollection;
    private readonly ILoadingOperations _loadingOperations;
    private ImageInfo _previewedImageInfo;
    private ImageSource _alternateImageView;

    private ImageSource _currentImageView;
    private ImageSource _primaryImageView;
    private readonly IPhotoDbService _dbService;
    private readonly IThumbnailService _thumbnailService;

    private long _allocMem;

    private RangeObservableCollection<ImageInfo> _imageCollection;

    private readonly Stack<(string OriginalPath, string DeletedPath)> _undoStack = new();

    private string _calcProgressText;

    private bool _includeSubfolders;

    private double _scoreThreshold = 200;

    private string _statusText = "Ready";
    private const string DeletedFolderName = ".deleted";

    private CancellationTokenSource _cts;

    private Timer _t;

    private IDisposable _scoreThresholdSubscription;
    private IDisposable _pipelineSubscription;
    private readonly IScheduler _backgroundScheduler;
    private readonly IScheduler _uiScheduler;

    private Visibility _isProgrVisible = Visibility.Collapsed;

    private Visibility _pairGridVisibility = Visibility.Collapsed;

    private Visibility _thumbnailGridVisibility = Visibility.Visible;

    public MainViewModel(
        IEventAggregator eventAggregator,
        IPhotoDbService dbService,
        ICalcOperations calcOperations,
        ILoadingOperations loadingOperations,
        IThumbnailService thumbnailService,
        IScheduler backgroundScheduler = null,
        IScheduler uiScheduler = null)
        : base(eventAggregator)
    {
        _dbService = dbService;
        _calcOperations = calcOperations;
        _loadingOperations = loadingOperations;
        _thumbnailService = thumbnailService;

        _backgroundScheduler = backgroundScheduler ?? TaskPoolScheduler.Default;

        _uiScheduler = uiScheduler
            ?? (SynchronizationContext.Current != null
                ? new SynchronizationContextScheduler(SynchronizationContext.Current)
                : TaskPoolScheduler.Default as IScheduler);

        ThumbnailSize = 200;
        IsLoaded = true;
        PairDataCollection = new List<ImagePair>();
        ImageCollection = [];
        StartMemoryAmountPublishing();

        _scoreThresholdSubscription = Observable
            .FromEventPattern<PropertyChangedEventHandler, PropertyChangedEventArgs>(
                h => PropertyChanged += h,
                h => PropertyChanged -= h)
            .Where(e => e.EventArgs.PropertyName == nameof(ScoreThreshold))
            .Throttle(TimeSpan.FromMilliseconds(200), _backgroundScheduler)
            .ObserveOn(_uiScheduler)
            .Subscribe(_ => RefilterPairs());

        PreviewSize = GridWidth;
        _ = InitializeDbAsync();
    }

    public long AllocMem
    {
        get => _allocMem;
        set => SetProperty(ref _allocMem, value);
    }

    /// <summary>Manually re-run the analysis (SIFT hashing + matching).</summary>
    public AsyncDelegateCommand AnalyzeCommand { get; private set; }

    public DelegateCommand CancelCommand { get; private set; }

    public double CalcProgress
    {
        get => _calcProgress;
        set
        {
            if (SetProperty(ref _calcProgress, value))
            {
                CalcProgressText = value.ToString("F1") + '%';
            }
        }
    }

    public string CalcProgressText
    {
        get => _calcProgressText;
        set => SetProperty(ref _calcProgressText, value);
    }

    public DelegateCommand CloseViewCommand { get; private set; }

    public ImageSource CurrentImageView
    {
        get => _currentImageView;
        set
        {
            if (SetProperty(ref _currentImageView, value))
            {
                RaisePropertyChanged(nameof(PreviewDeleteVisibility));
            }
        }
    }

    public Visibility PreviewDeleteVisibility => _currentImageView != null ? Visibility.Visible : Visibility.Collapsed;

    public AsyncDelegateCommand<ImageInfo> DeleteImageCommand { get; private set; }
    public AsyncDelegateCommand DeletePreviewCommand { get; private set; }

    public double GridWidth => 2 * ThumbnailSize + 80;

    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set => SetProperty(ref _includeSubfolders, value);
    }

    public RangeObservableCollection<ImageInfo> ImageCollection
    {
        get => _imageCollection;
        set => SetProperty(ref _imageCollection, value);
    }

    public bool IsLoaded
    {
        get => _isLoaded;
        set => SetProperty(ref _isLoaded, value);
    }

    public Visibility IsProgressVisible
    {
        get => _isProgrVisible;
        set => SetProperty(ref _isProgrVisible, value);
    }

    /// <summary>Open folder, scan images, load thumbnails, auto-start hashing.</summary>
    public AsyncDelegateCommand OpenCommand { get; private set; }

    public IList<ImagePair> PairDataCollection
    {
        get => _pairDataCollection;
        set => SetProperty(ref _pairDataCollection, value);
    }

    public Visibility PairGridVisibility
    {
        get => _pairGridVisibility;
        set => SetProperty(ref _pairGridVisibility, value);
    }

    public double PreviewSize
    {
        get => _previewSize;
        set => SetProperty(ref _previewSize, value);
    }

    public double ScoreThreshold
    {
        get => _scoreThreshold;
        set => SetProperty(ref _scoreThreshold, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public Visibility ThumbnailGridVisibility
    {
        get => _thumbnailGridVisibility;
        set => SetProperty(ref _thumbnailGridVisibility, value);
    }

    public ushort ThumbnailSize { get; }
    public AsyncDelegateCommand UndoCommand { get; private set; }

    public Visibility UndoVisibility => _undoStack.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public DelegateCommand<double?> ZoomInCommand { get; private set; }
    public DelegateCommand<double?> ZoomOutCommand { get; private set; }

    public void OpenView(string filePath, string alternatePath = null)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            var primaryImage = LoadImageFromFile(filePath);
            if (primaryImage == null)
            {
                return;
            }

            _primaryImageView = primaryImage;
            _alternateImageView = null;
            CurrentImageView = primaryImage;
            _previewedImageInfo = _dataCollectionFlat.FirstOrDefault(im => im.FilePath == filePath);
            DeletePreviewCommand.RaiseCanExecuteChanged();

            if (primaryImage.PixelHeight > 0)
            {
                var imgAspect = (double)primaryImage.PixelWidth / primaryImage.PixelHeight;
                const double defaultAreaHeight = 600;
                const double defaultAreaWidth = 800;
                PreviewSize = Math.Min(defaultAreaHeight, defaultAreaWidth / imgAspect);
            }

            if (alternatePath != null && File.Exists(alternatePath))
            {
                _alternateImageView = LoadImageFromFile(alternatePath);
            }
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message);
        }
    }

    public void RestorePrimaryPreview()
    {
        if (_primaryImageView != null)
        {
            CurrentImageView = _primaryImageView;
        }
    }

    public void ShowAlternatePreview()
    {
        if (_alternateImageView != null)
        {
            CurrentImageView = _alternateImageView;
        }
    }

    protected override void DefineCommands()
    {
        OpenCommand = new AsyncDelegateCommand(ExecuteOpenAsync);
        AnalyzeCommand = new AsyncDelegateCommand(ExecuteAnalyzeAsync);
        CancelCommand = new DelegateCommand(ExecuteCancel, () => _cts != null && !_cts.IsCancellationRequested);
        CloseViewCommand = new DelegateCommand(ExecuteCloseView);
        DeleteImageCommand = new AsyncDelegateCommand<ImageInfo>(ExecuteDeleteImage, _ => IsLoaded);
        DeletePreviewCommand = new AsyncDelegateCommand(
            async ct => { if (_previewedImageInfo != null)
                {
                    await ExecuteDeleteImage(_previewedImageInfo, ct);
                }
            },
            () => _previewedImageInfo != null);
        UndoCommand = new AsyncDelegateCommand(ExecuteUndo, () => _undoStack.Count > 0);
        ZoomInCommand = new DelegateCommand<double?>(ExecuteZoomIn);
        ZoomOutCommand = new DelegateCommand<double?>(ExecuteZoomOut);
    }

    protected override void DefineEvents()
    {
        EventAggregator?.GetEvent<OpenImagePreviewEvent>()
            .Subscribe(OnOpenImagePreview, ThreadOption.UIThread);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scoreThresholdSubscription?.Dispose();
            _scoreThresholdSubscription = null;
            _pipelineSubscription?.Dispose();
            _pipelineSubscription = null;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _t?.Dispose();
            _t = null;

            // Flush any queued thumbnails to DB before shutting down,
            // so progress is not lost when the user closes mid-pipeline.
            // Run on threadpool to avoid SynchronizationContext deadlock on UI thread.
            try { Task.Run(() => _dbService?.FlushThumbnailQueueAsync()).GetAwaiter().GetResult(); }
            catch { /* best-effort */ }

            _dbService?.Dispose();
        }
        base.Dispose(disposing);
    }

    private static BitmapImage LoadImageFromFile(string filePath)
    {
        using var sourceMat = new Mat(filePath);
        using var ms = sourceMat.ToMemoryStream(".jpg");

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private async Task ExecuteAnalyzeAsync()
    {
        if (_dataCollectionFlat == null || _dataCollectionFlat.Count == 0)
        {
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        CancelCommand.RaiseCanExecuteChanged();

        ThumbnailGridVisibility = Visibility.Visible;
        PairGridVisibility = Visibility.Collapsed;
        StatusText = "Starting analysis...";

        try
        {
            await RunAnalysisAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Analysis cancelled.";
            IsProgressVisible = Visibility.Collapsed;
        }
        finally
        {
            CancelCommand.RaiseCanExecuteChanged();
        }
    }

    private void ExecuteCloseView()
    {
        CurrentImageView = null;
        _previewedImageInfo = null;
        DeletePreviewCommand?.RaiseCanExecuteChanged();
        PreviewSize = GridWidth;
    }

    private async Task ExecuteDeleteImage(ImageInfo imageInfo, CancellationToken cancellationToken)
    {
        if (imageInfo == null || string.IsNullOrEmpty(imageInfo.FilePath))
        {
            return;
        }

        try
        {
            var filePath = imageInfo.FilePath;
            var directory = Path.GetDirectoryName(filePath);
            if (directory == null)
            {
                return;
            }

            // File I/O off the UI thread
            var destPath = await Task.Run(() =>
            {
                var deletedDir = Path.Combine(directory, DeletedFolderName);
                Directory.CreateDirectory(deletedDir);

                var fileName = Path.GetFileName(filePath);
                var dest = Path.Combine(deletedDir, fileName);

                // Handle name collision in .deleted folder
                if (File.Exists(dest))
                {
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);
                    var counter = 1;
                    do
                    {
                        dest = Path.Combine(deletedDir, $"{nameWithoutExt}_{counter}{ext}");
                        counter++;
                    } while (File.Exists(dest));
                }

                File.Move(filePath, dest);
                return dest;
            }, cancellationToken);

            _undoStack.Push((filePath, destPath));
            UndoCommand.RaiseCanExecuteChanged();
            RaisePropertyChanged(nameof(UndoVisibility));

            // Close preview if showing the deleted image
            ExecuteCloseView();

            // Remove from thumbnail collection
            ImageCollection.Remove(imageInfo);
            _dataCollectionFlat.Remove(imageInfo);

            // Remove all pairs containing this image
            if (PairDataCollection is List<ImagePair> pairs)
            {
                var remaining = pairs
                    .Where(p => p.Image1?.FilePath != filePath && p.Image2?.FilePath != filePath)
                    .ToList();
                PairDataCollection = remaining;

                if (remaining.Count == 0 && PairGridVisibility == Visibility.Visible)
                {
                    PairGridVisibility = Visibility.Collapsed;
                    ThumbnailGridVisibility = Visibility.Visible;
                    StatusText = "All pairs resolved.";
                }
                else
                {
                    StatusText = $"{remaining.Count} pairs remaining.";
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to delete image: {ex.Message}");
        }
    }

    private void ExecuteCancel()
    {
        _cts?.Cancel();
        StatusText = "Cancelling...";
    }

    private async Task ExecuteOpenAsync()
    {
        // Show folder dialog on UI thread
        var rootFolder = _loadingOperations.ShowFolderDialog();
        if (rootFolder == null)
        {
            return;
        }

        // Cancel any previous operation
        if (_cts != null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }
        _cts = new CancellationTokenSource();
        CancelCommand.RaiseCanExecuteChanged();
        var ct = _cts.Token;

        ThumbnailGridVisibility = Visibility.Visible;
        PairGridVisibility = Visibility.Collapsed;
        PairDataCollection = new List<ImagePair>();
        IsProgressVisible = Visibility.Visible;
        CalcProgress = 0;

        using var openOp = PerfLogger.TimedVerbose("OPEN");
        openOp.Detail($"folder={rootFolder}");

        try
        {
            List<FileInfo> fileInfos;
            using (var scanOp = PerfLogger.TimedVerbose("SCAN"))
            {
                fileInfos = await ScanFolderAsync(rootFolder, ct);
                scanOp.Detail($"files={fileInfos.Count}");
            }

            if (fileInfos.Count == 0)
            {
                StatusText = "No images found in selected folder.";
                IsProgressVisible = Visibility.Collapsed;
                return;
            }

            _dataCollectionFlat = new List<ImageInfo>(fileInfos.Count);
            ImageCollection = [];

            await BuildAndLoadStreamingAsync(fileInfos, ct);

            ct.ThrowIfCancellationRequested();

            if (await TryLoadCachedPairsAsync())
            {
                return;
            }

            StatusText = "Thumbnails loaded. Starting analysis...";
            await RunAnalysisAsync(ct);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Operation cancelled.";
            IsProgressVisible = Visibility.Collapsed;
        }
        finally
        {
            CancelCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Phase 1: Scans the selected folder for image files in background.
    /// Returns FileInfo objects with pre-fetched metadata (fast on network shares).
    /// </summary>
    private async Task<List<FileInfo>> ScanFolderAsync(string rootFolder, CancellationToken ct)
    {
        StatusText = "Scanning folder...";
        var scanProgress = new Progress<int>(count =>
        {
            StatusText = $"Scanning folder... {count} images found";
        });

        return await Task.Run(() =>
            _loadingOperations.ScanImageFileInfos(rootFolder, IncludeSubfolders, scanProgress, ct), ct);
    }

    /// <summary>
    /// Streaming pipeline: creates ImageInfo objects and loads thumbnails incrementally.
    /// Items appear in ImageCollection as soon as they're created; thumbnails load concurrently.
    /// Pre-loads the DB thumbnail cache for fast O(1) lookups, and batches UI additions
    /// to reduce ObservableCollection notification overhead.
    /// </summary>
    private async Task BuildAndLoadStreamingAsync(List<FileInfo> fileInfos, CancellationToken ct)
    {
        using var pipelineOp = PerfLogger.TimedVerbose("PIPELINE");

        var totalFiles = fileInfos.Count;
        var ea = EventAggregator;
        var tcs = new TaskCompletionSource<bool>();
        var createdCount = 0;
        var loadedCount = 0;
        var mergeParallelism = Math.Max(Environment.ProcessorCount - 1, 1);

        pipelineOp.Detail($"totalFiles={totalFiles}");
        pipelineOp.Detail($"mergeParallelism={mergeParallelism}");

        // Pre-load all cached thumbnails into memory to avoid per-image DB queries
        StatusText = "Preloading cache...";
        using (PerfLogger.TimedVerbose("PRELOAD_CACHE"))
            await _dbService.PreloadThumbnailCacheAsync();
        ct.ThrowIfCancellationRequested();

        // Tracking for thumbnail throughput logging
        var thumbSw = Stopwatch.StartNew();
        long thumbSlowCount = 0;       // thumbnails taking > 500ms
        long thumbVerySlowCount = 0;    // thumbnails taking > 2000ms
        var lastProgressLog = Stopwatch.StartNew();

        var cancel = Observable.Create<bool>(obs =>
            ct.Register(() => { obs.OnNext(true); obs.OnCompleted(); }));

        _pipelineSubscription = fileInfos
            .ToObservable(_backgroundScheduler)
            .TakeUntil(cancel)
            .Select(fi => new ImageInfo(fi, ea))
            // Buffer image creation in batches to reduce UI thread context switches
            .Buffer(100)
            .ObserveOn(_uiScheduler)
            .Do(batch =>
            {
                using var batchOp = PerfLogger.TimedVerbose("BATCH_ADD");

                foreach (var img in batch)
                {
                    _dataCollectionFlat.Add(img);
                }

                batchOp.Lap("listAdd");

                ImageCollection.AddRange(batch);
                batchOp.Lap("addRange");

                createdCount += batch.Count;
                CalcProgress = (double)createdCount / totalFiles * 50.0;
                StatusText = $"Loading images... {createdCount}/{totalFiles}";

                batchOp.Detail($"batchSize={batch.Count}");
                batchOp.Detail($"totalAdded={createdCount}/{totalFiles}");
                batchOp.Detail($"collectionSize={ImageCollection.Count}");
            })
            // Flatten batches back to individual items for thumbnail loading
            .SelectMany(batch => batch.ToObservable())
            .ObserveOn(_backgroundScheduler)
            .Select(img => Observable.FromAsync(async () =>
            {
                var imgSw = Stopwatch.StartNew();
                await img.LoadThumbnailAsync(_dbService, _thumbnailService);
                imgSw.Stop();

                var ms = imgSw.ElapsedMilliseconds;
                switch (ms)
                {
                    case > 2000:
                        Interlocked.Increment(ref thumbVerySlowCount);
                        break;
                    case > 500:
                        Interlocked.Increment(ref thumbSlowCount);
                        break;
                }

                return img;
            }))
            .Merge(mergeParallelism)
            .ObserveOn(_uiScheduler)
            .Do(img =>
            {
                var count = Interlocked.Increment(ref loadedCount);
                CalcProgress = 50.0 + (double)count / totalFiles * 50.0;
                StatusText = $"Loading thumbnails... {count}/{totalFiles}";

                // Periodically flush cached thumbnails to DB so progress
                // survives if the user closes the app mid-pipeline.
                if (count % 200 == 0)
                {
                    var db = _dbService;
                    _ = Task.Run(async () =>
                    {
                        try { await db.FlushThumbnailQueueAsync(); }
                        catch { /* best-effort */ }
                    });
                }

                // Log throughput every 500 thumbnails or every 10 seconds
                if (count % 500 == 0 || lastProgressLog.ElapsedMilliseconds > 10000)
                {
                    var elapsed = thumbSw.Elapsed.TotalSeconds;
                    var rate = elapsed > 0 ? count / elapsed : 0;
                    PerfLogger.Verbose($"[PERF] THUMB_PROGRESS | loaded={count}/{totalFiles} " +
                                    $"| elapsed={elapsed:F1}s | rate={rate:F1}/s " +
                                    $"| slow500ms={Interlocked.Read(ref thumbSlowCount)} " +
                                    $"| verySlow2s={Interlocked.Read(ref thumbVerySlowCount)} " +
                                    $"| lastFile={img.FileName} | lastSize={img.FileSize}");
                    lastProgressLog.Restart();
                }
            })
            .Subscribe(
                _ => { },
                ex => tcs.TrySetException(ex),
                () => tcs.TrySetResult(!ct.IsCancellationRequested));

        var completed = await tcs.Task;
        pipelineOp.Lap("thumbnails");

        pipelineOp.Detail($"totalLoaded={loadedCount}");
        pipelineOp.Detail($"slow500ms={Interlocked.Read(ref thumbSlowCount)}");
        pipelineOp.Detail($"verySlow2s={Interlocked.Read(ref thumbVerySlowCount)}");

        // Batch-write all new thumbnails to DB and free the in-memory cache
        StatusText = "Saving cache...";
        using (PerfLogger.TimedVerbose("FLUSH_CACHE"))
            await _dbService.FlushThumbnailQueueAsync();
        _dbService.ClearThumbnailCache();

        if (!completed)
        {
            throw new OperationCanceledException(ct);
        }
    }

    private async Task ExecuteUndo()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        var (originalPath, deletedPath) = _undoStack.Pop();
        UndoCommand.RaiseCanExecuteChanged();

        try
        {
            if (!File.Exists(deletedPath))
            {
                StatusText = "Undo failed: deleted file not found.";
                return;
            }

            File.Move(deletedPath, originalPath);

            // Re-add the image to the collection
            var restoredImage = new ImageInfo(originalPath, EventAggregator);
            _dataCollectionFlat.Add(restoredImage);
            ImageCollection.Add(restoredImage);

            // Load thumbnail in background
            await restoredImage.LoadThumbnailAsync(_dbService, _thumbnailService);

            // Try to reload cached pairs (will quickly restore pairs from DB hash)
            await TryLoadCachedPairsAsync();

            StatusText = $"Restored: {Path.GetFileName(originalPath)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Undo failed: {ex.Message}");
        }
    }

    private void ExecuteZoomIn(double? p)
    {
        if (p.HasValue)
        {
            PreviewSize = p.Value * 1.1 + 20;
        }
    }

    private void ExecuteZoomOut(double? p)
    {
        if (p.HasValue)
        {
            PreviewSize = p.Value * 0.9 + 20;
        }
    }

    private async Task InitializeDbAsync()
    {
        try
        {
            await _dbService.InitializeAsync();
        }
        catch (Exception ex)
        {
            PerfLogger.Log($"DB initialization failed: {ex.Message}");
        }
    }

    private void OnOpenImagePreview(OpenImagePreviewPayload payload)
    {
        if (payload != null)
        {
            OpenView(payload.FilePath, payload.AlternatePath);
        }
    }

    private void PopulateDupes()
    {
        var temp = _matches?
            .Where(match => match.Match < ScoreThreshold)
            .Select(match => new ImagePair(ThumbnailSize, EventAggregator)
            {
                Image1 = _dataCollectionFlat.FirstOrDefault(im => im.FilePath == match.Hash1.Key),
                Image2 = _dataCollectionFlat.FirstOrDefault(im => im.FilePath == match.Hash2.Key),
                Match = match.Match
            }).ToList();

        if (temp is { Count: > 0 })
        {
            PairDataCollection = temp;
            ThumbnailGridVisibility = Visibility.Collapsed;
            PairGridVisibility = Visibility.Visible;
            StatusText = $"Analysis complete. Found {temp.Count} pairs.";
        }
        else
        {
            StatusText = "Analysis complete. No similar pairs found.";
        }
    }

    private void RefilterPairs()
    {
        if (_matches == null)
        {
            return;
        }

        PopulateDupes();
    }

    /// <summary>
    /// Runs the full SIFT hashing and similarity matching pipeline asynchronously.
    /// </summary>
    private async Task RunAnalysisAsync(CancellationToken ct = default)
    {
        if (_dataCollectionFlat == null || _dataCollectionFlat.Count < 2)
        {
            StatusText = "Need at least 2 images to analyze.";
            return;
        }

        IsProgressVisible = Visibility.Visible;
        var imageCount = _dataCollectionFlat.Count;

        // Phase: Hashing
        StatusText = $"Computing hashes... 0/{imageCount}";
        var hashProgress = new Progress<double>(value =>
        {
            CalcProgress = value;
            var done = (int)(value / 100.0 * imageCount);
            StatusText = $"Computing hashes... {done}/{imageCount}";
        });

        var hashesDict = _calcOperations.CalcSiftHashes(
            _dataCollectionFlat, _dbService, hashProgress, out var hashTask, ThumbnailSize, ct);

        await hashTask;
        ct.ThrowIfCancellationRequested();

        // Phase: Matching
        var pairCount = hashesDict.Count * (hashesDict.Count - 1) / 2;
        StatusText = $"Matching pairs... 0/{pairCount}";
        var matchProgress = new Progress<double>(value =>
        {
            CalcProgress = value;
            var done = (int)(value / 100.0 * pairCount);
            StatusText = $"Matching pairs... {done}/{pairCount}";
        });

        _matches = await Task.Run(() =>
            _calcOperations.CreateMatchCollection(hashesDict, matchProgress, ct).Distinct().ToList(), ct);

        ct.ThrowIfCancellationRequested();

        await StoreSimilarityResultsAsync(_matches);

        IsProgressVisible = Visibility.Collapsed;
        PopulateDupes();
    }

    private void StartMemoryAmountPublishing()
    {
        _t = new Timer(_ => Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            using var proc = Process.GetCurrentProcess();
            AllocMem = proc.PrivateMemorySize64 / 1000000;
        })), null, 0, 300);
    }

    /// <summary>
    /// Persists computed similarity results to the database.
    /// Looks up each photo's DB ID by file path and stores the pair score.
    /// </summary>
    private async Task StoreSimilarityResultsAsync(IEnumerable<PairSimilarityInfo> matches)
    {
        if (_dbService is not { IsAvailable: true } || matches == null)
        {
            return;
        }

        var matchList = matches.Where(m => m.Match < double.MaxValue).ToList();
        if (matchList.Count == 0)
        {
            return;
        }

        // Collect all unique file paths
        var uniquePaths = new HashSet<string>();
        foreach (var match in matchList)
        {
            uniquePaths.Add(match.Hash1.Key);
            uniquePaths.Add(match.Hash2.Key);
        }

        // Build path-to-ID lookup with one DB call per unique path
        var pathToId = new Dictionary<string, int>(uniquePaths.Count);
        foreach (var path in uniquePaths)
        {
            try
            {
                var photo = await _dbService.GetCachedPhotoAsync(path);
                if (photo != null)
                {
                    pathToId[path] = photo.Id;
                }
            }
            catch (Exception ex)
            {
                PerfLogger.Log($"DB photo lookup error for '{path}': {ex.Message}");
            }
        }

        // Store similarity scores using pre-resolved IDs
        foreach (var match in matchList)
        {
            try
            {
                if (pathToId.TryGetValue(match.Hash1.Key, out var id1) &&
                    pathToId.TryGetValue(match.Hash2.Key, out var id2))
                {
                    await _dbService.StoreSimilarityAsync(id1, id2, match.Match);
                }
            }
            catch (Exception ex)
            {
                PerfLogger.Log($"DB similarity store error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Attempts to load cached similarity results from the database.
    /// Returns true if cached pairs were found and populated.
    /// </summary>
    private async Task<bool> TryLoadCachedPairsAsync()
    {
        if (_dbService is not { IsAvailable: true })
        {
            return false;
        }

        if (_dataCollectionFlat == null || _dataCollectionFlat.Count < 2)
        {
            return false;
        }

        try
        {
            var cachedResults = await _dbService.GetCachedResultsAsync();
            if (cachedResults == null || cachedResults.Count == 0)
            {
                return false;
            }

            var currentPaths = new HashSet<string>(_dataCollectionFlat.Select(i => i.FilePath));

            var relevantResults = cachedResults
                .Where(r => r.Photo1?.FilePath != null && currentPaths.Contains(r.Photo1.FilePath)
                            && r.Photo2?.FilePath != null && currentPaths.Contains(r.Photo2.FilePath))
                .ToList();

            if (relevantResults.Count == 0)
            {
                return false;
            }

            // Populate _matches so the slider can re-filter cached results too
            _matches = relevantResults
                .Select(r => new PairSimilarityInfo(
                    new KeyValuePair<string, Mat>(r.Photo1.FilePath, null),
                    new KeyValuePair<string, Mat>(r.Photo2.FilePath, null),
                    r.Score))
                .OrderBy(p => p.Match)
                .ToList();

            PopulateDupes();

            if (PairDataCollection is { Count: > 0 })
            {
                StatusText = $"Loaded {PairDataCollection.Count} cached pairs. Click Analyze to re-scan.";
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            PerfLogger.Log($"Failed to load cached results: {ex.Message}");
            return false;
        }
    }
}