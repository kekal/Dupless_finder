using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dupples_finder_UI.DTO;
using Dupples_finder_UI.Events;
using Dupples_finder_UI.Modules.Helpers;
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

    private ObservableCollection<ImageInfo> _imageCollection;

    private readonly Stack<(string OriginalPath, string DeletedPath)> _undoStack = new();

    private string _calcProgressText;

    private bool _includeSubfolders;

    private double _scoreThreshold = 200;

    private string _statusText = "Ready";
    private const string DeletedFolderName = ".deleted";

    private CancellationTokenSource _cts;

    private Timer _t;

    private Visibility _isProgrVisible = Visibility.Collapsed;

    private Visibility _pairGridVisibility = Visibility.Collapsed;

    private Visibility _thumbnailGridVisibility = Visibility.Visible;

    public MainViewModel(
        IEventAggregator eventAggregator,
        IPhotoDbService dbService,
        ICalcOperations calcOperations,
        ILoadingOperations loadingOperations,
        IThumbnailService thumbnailService)
        : base(eventAggregator)
    {
        _dbService = dbService;
        _calcOperations = calcOperations;
        _loadingOperations = loadingOperations;
        _thumbnailService = thumbnailService;

        ThumbnailSize = 200;
        IsLoaded = true;
        PairDataCollection = new List<ImagePair>();
        ImageCollection = [];
        StartMemoryAmountPublishing();

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

    public ObservableCollection<ImageInfo> ImageCollection
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
        set
        {
            if (SetProperty(ref _scoreThreshold, value))
            {
                RefilterPairs();
            }
        }
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
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _t?.Dispose();
            _t = null;
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

        try
        {
            var fileInfos = await ScanFolderAsync(rootFolder, ct);
            if (fileInfos.Count == 0)
            {
                StatusText = "No images found in selected folder.";
                IsProgressVisible = Visibility.Collapsed;
                return;
            }

            var imageList = await BuildImageListAsync(fileInfos, ct);

            _dataCollectionFlat = imageList;
            ImageCollection = new ObservableCollection<ImageInfo>(imageList);

            await LoadAllThumbnailsAsync(imageList, ct);

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
    /// Phase 2: Creates ImageInfo objects from pre-fetched FileInfo metadata in background.
    /// Uses the FileInfo constructor overload to avoid per-file stat calls on network shares.
    /// </summary>
    private async Task<List<ImageInfo>> BuildImageListAsync(List<FileInfo> fileInfos, CancellationToken ct)
    {
        var totalFiles = fileInfos.Count;
        StatusText = $"Reading file info... 0/{totalFiles}";
        CalcProgress = 0;
        var ea = EventAggregator;

        return await Task.Run(() =>
        {
            var list = new List<ImageInfo>(totalFiles);
            var progressStep = totalFiles > 0 ? 100.0 / totalFiles : 100.0;
            var reportInterval = Math.Max(totalFiles / 200, 1);

            for (var i = 0; i < fileInfos.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                list.Add(new ImageInfo(fileInfos[i], ea));

                if (i % reportInterval == 0 || i == totalFiles - 1)
                {
                    var idx = i;
                    var progress = (i + 1) * progressStep;
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        CalcProgress = Math.Min(progress, 100.0);
                        StatusText = $"Reading file info... {idx + 1}/{totalFiles}";
                    }));
                }
            }

            return list;
        }, ct);
    }

    /// <summary>
    /// Phase 3: Loads thumbnails for all images in background with progress reporting.
    /// </summary>
    private async Task LoadAllThumbnailsAsync(IList<ImageInfo> imageList, CancellationToken ct)
    {
        StatusText = $"Loading thumbnails... 0/{imageList.Count}";
        await Task.Run(() => LoadThumbnailsInBackground(imageList, ct), ct);
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
            await Task.Run(() => restoredImage.LoadThumbnail(_dbService, _thumbnailService));

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
            Trace.WriteLine($"DB initialization failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads thumbnails for all images in background with progress reporting.
    /// Uses DB cache first, then OS Shell API for generation.
    /// Designed to be called from a background thread via Task.Run.
    /// </summary>
    private void LoadThumbnailsInBackground(IList<ImageInfo> images, CancellationToken ct = default)
    {
        var imageCount = images.Count;

        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            CalcProgress = 0;
            IsProgressVisible = Visibility.Visible;
        }));

        var progressStep = imageCount > 0 ? 100.0 / imageCount : 100.0;
        int[] completed = [0];

        Parallel.ForEach(images,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(Environment.ProcessorCount - 1, 1),
                CancellationToken = ct
            },
            info =>
            {
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

                info.LoadThumbnail(_dbService, _thumbnailService);

                var done = Interlocked.Increment(ref completed[0]);
                var progress = done * progressStep;

                if (done % 10 == 0)
                {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    CalcProgress = Math.Min(progress, 100.0);
                    StatusText = $"Loading thumbnails... {done}/{imageCount}";
                }));
                }
            });

        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            CalcProgress = 100;
            IsProgressVisible = Visibility.Collapsed;
        }));
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
            //using var proc = Process.GetCurrentProcess();
            //AllocMem = proc.PrivateMemorySize64 / 1000000;
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

        foreach (var match in matches)
        {
            if (match.Match >= double.MaxValue)
            {
                continue;
            }

            try
            {
                var photo1 = await _dbService.GetCachedPhotoAsync(match.Hash1.Key);
                var photo2 = await _dbService.GetCachedPhotoAsync(match.Hash2.Key);
                if (photo1 != null && photo2 != null)
                {
                    await _dbService.StoreSimilarityAsync(photo1.Id, photo2.Id, match.Match);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"DB similarity store error: {ex.Message}");
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
            Trace.WriteLine($"Failed to load cached results: {ex.Message}");
            return false;
        }
    }
}