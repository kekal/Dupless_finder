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

namespace Dupples_finder_UI.Modules.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IPhotoDbService _dbService;
        private readonly ICalcOperations _calcOperations;
        private readonly ILoadingOperations _loadingOperations;
        private readonly IThumbnailService _thumbnailService;

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

        /// <summary>
        /// Initializes the EF Core SQLite database service.
        /// Runs asynchronously; if it fails, the app continues without caching.
        /// </summary>
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

       

        // ==================================================================================================================

        #region Fields

        private IEnumerable<PairSimilarityInfo> _matches;
        private ImageSource _primaryImageView;
        private ImageSource _alternateImageView;

        // Keep a flat list for SIFT hashing (same items as ImageCollection)
        private IList<ImageInfo> _dataCollectionFlat = new List<ImageInfo>();

        #endregion

        // ==================================================================================================================

        #region Properties

        public ushort ThumbnailSize { get; }

        /// <summary>Thumbnail list width computed from ThumbnailSize.</summary>
        public double GridWidth => 2 * ThumbnailSize + 80;

        private ObservableCollection<ImageInfo> _imageCollection;
        /// <summary>
        /// Observable collection of individual images for the thumbnail grid.
        /// Bound to the UI -- items appear immediately, thumbnails fill in progressively.
        /// </summary>
        public ObservableCollection<ImageInfo> ImageCollection
        {
            get => _imageCollection;
            set => SetProperty(ref _imageCollection, value);
        }

        private IList<ImagePair> _pairDataCollection;
        public IList<ImagePair> PairDataCollection
        {
            get => _pairDataCollection;
            set => SetProperty(ref _pairDataCollection, value);
        }

        private bool _isLoaded;
        public bool IsLoaded
        {
            get => _isLoaded;
            set => SetProperty(ref _isLoaded, value);
        }

        private Visibility _isProgrVisible = Visibility.Collapsed;
        public Visibility IsProgressVisible
        {
            get => _isProgrVisible;
            set => SetProperty(ref _isProgrVisible, value);
        }

        private double _calcProgress;
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

        private string _calcProgressText;
        public string CalcProgressText
        {
            get => _calcProgressText;
            set => SetProperty(ref _calcProgressText, value);
        }

        private long _allocMem;
        public long AllocMem
        {
            get => _allocMem;
            set => SetProperty(ref _allocMem, value);
        }

        private ImageSource _currentImageView;
        public ImageSource CurrentImageView
        {
            get => _currentImageView;
            set => SetProperty(ref _currentImageView, value);
        }

        private double _previewSize;
        public double PreviewSize
        {
            get => _previewSize;
            set => SetProperty(ref _previewSize, value);
        }

        private Visibility _thumbnailGridVisibility = Visibility.Visible;
        /// <summary>
        /// Visibility of the individual thumbnail grid (shown before analysis).
        /// </summary>
        public Visibility ThumbnailGridVisibility
        {
            get => _thumbnailGridVisibility;
            set => SetProperty(ref _thumbnailGridVisibility, value);
        }

        private Visibility _pairGridVisibility = Visibility.Collapsed;
        /// <summary>
        /// Visibility of the pair DataGrid (shown after analysis).
        /// </summary>
        public Visibility PairGridVisibility
        {
            get => _pairGridVisibility;
            set => SetProperty(ref _pairGridVisibility, value);
        }

        private string _statusText = "Ready";
        /// <summary>
        /// Status text shown in the toolbar to indicate current operation.
        /// </summary>
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        #endregion

        // ==================================================================================================================

        #region Commands

        /// <summary>Open folder, scan images, load thumbnails, auto-start hashing.</summary>
        public AsyncDelegateCommand OpenCommand { get; private set; }

        /// <summary>Manually re-run the analysis (SIFT hashing + matching).</summary>
        public AsyncDelegateCommand AnalyzeCommand { get; private set; }

        public DelegateCommand CloseViewCommand { get; private set; }
        public DelegateCommand<double?> ZoomInCommand { get; private set; }
        public DelegateCommand<double?> ZoomOutCommand { get; private set; }

        protected override void DefineCommands()
        {
            OpenCommand = new AsyncDelegateCommand(ExecuteOpenAsync);
            AnalyzeCommand = new AsyncDelegateCommand(ExecuteAnalyzeAsync);
            CloseViewCommand = new DelegateCommand(ExecuteCloseView);
            ZoomInCommand = new DelegateCommand<double?>(ExecuteZoomIn);
            ZoomOutCommand = new DelegateCommand<double?>(ExecuteZoomOut);
        }

        private async Task ExecuteOpenAsync()
        {
            // Step 1: Open folder dialog and scan for images (sync, on UI thread)
            if (!_loadingOperations.GetAllPaths(out var paths, string.Empty))
            {
                return;
            }

            // Reset state for new folder
            ThumbnailGridVisibility = Visibility.Visible;
            PairGridVisibility = Visibility.Collapsed;
            PairDataCollection = new List<ImagePair>();

            // Step 2: Create ImageInfo objects immediately (path + fingerprint, no images yet)
            var imageList = paths.Select(path => new ImageInfo(path, EventAggregator)).ToList();
            _dataCollectionFlat = imageList;

            // Step 3: Show all images in UI immediately as placeholders
            var collection = new ObservableCollection<ImageInfo>(imageList);
            ImageCollection = collection;

            StatusText = $"Found {imageList.Count} images. Loading thumbnails...";

            // Step 4: Background -- load thumbnails (DB first, then OS Shell)
            await Task.Run(() => LoadThumbnailsInBackground(imageList));

            // Step 5: Try cached results first; fall back to full SIFT analysis
            if (await TryLoadCachedPairsAsync())
            {
                return;
            }

            StatusText = "Thumbnails loaded. Starting analysis...";
            await RunAnalysisAsync();
        }

        private async Task ExecuteAnalyzeAsync()
        {
            if (_dataCollectionFlat == null || _dataCollectionFlat.Count == 0)
            {
                return;
            }

            ThumbnailGridVisibility = Visibility.Visible;
            PairGridVisibility = Visibility.Collapsed;
            StatusText = "Starting analysis...";

            await RunAnalysisAsync();
        }

        private void ExecuteCloseView()
        {
            CurrentImageView = null;
            PreviewSize = GridWidth;
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

        #endregion

        // ==================================================================================================================

        #region Events

        protected override void DefineEvents()
        {
            EventAggregator?.GetEvent<OpenImagePreviewEvent>()
                .Subscribe(OnOpenImagePreview, ThreadOption.UIThread);
        }

        private void OnOpenImagePreview(OpenImagePreviewPayload payload)
        {
            if (payload != null)
            {
                OpenView(payload.FilePath, payload.AlternatePath);
            }
        }

        #endregion

        // ==================================================================================================================

        #region Methods

        private Timer _t;

        private void StartMemoryAmountPublishing()
        {
            _t = new Timer(_ => Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                using var proc = Process.GetCurrentProcess();
                AllocMem = proc.PrivateMemorySize64 / 1000000;
            })), null, 0, 300);
        }

        /// <summary>
        /// Loads thumbnails for all images in background with progress reporting.
        /// Uses DB cache first, then OS Shell API for generation.
        /// Designed to be called from a background thread via Task.Run.
        /// </summary>
        private void LoadThumbnailsInBackground(IList<ImageInfo> images)
        {
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                CalcProgress = 0;
                IsProgressVisible = Visibility.Visible;
            }));

            double progressStep = images.Count > 0 ? 100.0 / images.Count : 100.0;
            int[] completed = [0];

            Parallel.ForEach(images,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(Environment.ProcessorCount - 1, 1) },
                info =>
                {
                    Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

                    info.LoadThumbnail(_dbService, _thumbnailService);

                    int done = Interlocked.Increment(ref completed[0]);
                    double progress = done * progressStep;
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        CalcProgress = Math.Min(progress, 100.0);
                    }));
                });

            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                CalcProgress = 100;
                IsProgressVisible = Visibility.Collapsed;
            }));
        }

        /// <summary>
        /// Runs the full SIFT hashing and similarity matching pipeline asynchronously.
        /// </summary>
        private async Task RunAnalysisAsync()
        {
            if (_dataCollectionFlat == null || _dataCollectionFlat.Count < 2)
            {
                StatusText = "Need at least 2 images to analyze.";
                return;
            }

            // Create IProgress<double> that updates CalcProgress on the UI thread
            var calcProgress = new Progress<double>(value =>
            {
                CalcProgress = value;
            });

            // CalcSiftHashes fires off parallel tasks and returns immediately
            var hashesDict = _calcOperations.CalcSiftHashes(
                _dataCollectionFlat, _dbService, calcProgress, out Task hashTask, ThumbnailSize);

            // Await completion of all SIFT hash tasks
            await hashTask;

            // Run CPU-bound matching on the thread pool
            var matchProgress = new Progress<double>(value =>
            {
                CalcProgress = value;
            });

            _matches = await Task.Run(() =>
                _calcOperations.CreateMatchCollection(hashesDict, matchProgress).Distinct().ToList());

            // Persist results to DB
            await StoreSimilarityResultsAsync(_matches);

            // Populate UI with results (runs on UI thread after await)
            PopulateDupes();
        }

        /// <summary>
        /// Persists computed similarity results to the database.
        /// Looks up each photo's DB ID by file path and stores the pair score.
        /// </summary>
        private async Task StoreSimilarityResultsAsync(IEnumerable<PairSimilarityInfo> matches)
        {
            if (_dbService == null || !_dbService.IsAvailable || matches == null)
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
            if (_dbService == null || !_dbService.IsAvailable)
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
                    .Where(r => r.Score < 200
                        && r.Photo1?.FilePath != null && currentPaths.Contains(r.Photo1.FilePath)
                        && r.Photo2?.FilePath != null && currentPaths.Contains(r.Photo2.FilePath))
                    .ToList();

                if (relevantResults.Count == 0)
                {
                    return false;
                }

                // Already on UI thread after await (SynchronizationContext)
                var pairs = relevantResults
                    .Select(r => new ImagePair(ThumbnailSize, EventAggregator)
                    {
                        Image1 = _dataCollectionFlat.FirstOrDefault(im => im.FilePath == r.Photo1.FilePath),
                        Image2 = _dataCollectionFlat.FirstOrDefault(im => im.FilePath == r.Photo2.FilePath),
                        Match = r.Score,
                    })
                    .Where(p => p.Image1 != null && p.Image2 != null)
                    .ToList();

                if (pairs.Count > 0)
                {
                    PairDataCollection = pairs;
                    ThumbnailGridVisibility = Visibility.Collapsed;
                    PairGridVisibility = Visibility.Visible;
                    StatusText = $"Loaded {pairs.Count} cached pairs. Click Analyze to re-scan.";
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

        private void PopulateDupes()
        {
            // Filter out pairs with too few good matches (score > 200 means < 5 matches)
            var temp = _matches?
                .Where(match => match.Match < 200)
                .Take(_dataCollectionFlat.Count)
                .Select(match => new ImagePair(ThumbnailSize, EventAggregator)
                {
                    Image1 = _dataCollectionFlat.FirstOrDefault(im => im.FilePath == match.Hash1.Key),
                    Image2 = _dataCollectionFlat.FirstOrDefault(im => im.FilePath == match.Hash2.Key),
                    Match = match.Match,
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _t?.Dispose();
                _t = null;
                _dbService?.Dispose();
            }
            base.Dispose(disposing);
        }

        #endregion

        // ==================================================================================================================

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

                // Fit preview to a reasonable default size
                if (primaryImage.PixelHeight > 0)
                {
                    double imgAspect = (double)primaryImage.PixelWidth / primaryImage.PixelHeight;
                    double defaultAreaHeight = 600;
                    double defaultAreaWidth = 800;
                    PreviewSize = Math.Min(defaultAreaHeight, defaultAreaWidth / imgAspect);
                }

                // Load the alternate image for mouse-press preview
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

        public void ShowAlternatePreview()
        {
            if (_alternateImageView != null)
            {
                CurrentImageView = _alternateImageView;
            }
        }

        public void RestorePrimaryPreview()
        {
            if (_primaryImageView != null)
            {
                CurrentImageView = _primaryImageView;
            }
        }
    }
}
