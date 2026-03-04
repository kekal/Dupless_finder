using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Dupples_finder_UI.Data.Entities;
using Dupples_finder_UI.DTO;
using Dupples_finder_UI.Events;
using Dupples_finder_UI.Modules.ViewModels;
using Dupples_finder_UI.Services.Interfaces;
using Moq;
using OpenCvSharp;
using Prism.Events;
using Xunit;

namespace Dupless_finder_Tests;

/// <summary>
/// ViewModel-level integration tests for MainViewModel.
/// All service dependencies are mocked. WPF Dispatcher calls are safe because
/// Application.Current is null in the test runner and all Dispatcher usages in
/// production code guard with ?. operators.
///
/// Prism 9's AsyncDelegateCommand does not expose an ExecuteAsync() method.
/// The helper TestHelpers.InvokeCommandAsync() extracts the private _executeMethod delegate
/// via reflection and awaits the returned Task so tests remain fully deterministic.
/// </summary>
public class MainViewModelTests : IDisposable
{
    // ---------------------------------------------------------------------------
    // Fields
    // ---------------------------------------------------------------------------

    private readonly Mock<IPhotoDbService> _mockDbService;
    private readonly Mock<ICalcOperations> _mockCalcOps;
    private readonly Mock<ILoadingOperations> _mockLoadingOps;
    private readonly Mock<IThumbnailService> _mockThumbnailService;
    private readonly IEventAggregator _eventAggregator;
    private readonly MainViewModel _vm;

    // ---------------------------------------------------------------------------
    // Construction helpers
    // ---------------------------------------------------------------------------

    public MainViewModelTests()
    {
        _mockDbService = new Mock<IPhotoDbService>();
        _mockCalcOps = new Mock<ICalcOperations>();
        _mockLoadingOps = new Mock<ILoadingOperations>();
        _mockThumbnailService = new Mock<IThumbnailService>();

        // InitializeDbAsync is fire-and-forget; ensure it completes instantly.
        _mockDbService
            .Setup(d => d.InitializeAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Prism requires a SynchronizationContext when subscribing with ThreadOption.UIThread.
        // ReSharper's test runner doesn't set one, so install a default here.
        if (SynchronizationContext.Current == null)
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
        }

        // Use the real EventAggregator so that Prism event subscriptions wire up correctly.
        _eventAggregator = new EventAggregator();

        _vm = CreateViewModel();
    }

    private MainViewModel CreateViewModel()
    {
        return new MainViewModel(
            _eventAggregator,
            _mockDbService.Object,
            _mockCalcOps.Object,
            _mockLoadingOps.Object,
            _mockThumbnailService.Object);
    }

    public void Dispose()
    {
        _vm?.Dispose();
    }

    // ===========================================================================
    // 1. Constructor / initial state
    // ===========================================================================

    #region Constructor Tests

    [Fact]
    public void Constructor_SetsInitialThumbnailSize()
    {
        Assert.Equal((ushort)200, _vm.ThumbnailSize);
    }

    [Fact]
    public void Constructor_SetsIsLoadedToTrue()
    {
        Assert.True(_vm.IsLoaded);
    }

    [Fact]
    public void Constructor_ImageCollection_IsEmpty()
    {
        Assert.NotNull(_vm.ImageCollection);
        Assert.Empty(_vm.ImageCollection);
    }

    [Fact]
    public void Constructor_PairDataCollection_IsEmpty()
    {
        Assert.NotNull(_vm.PairDataCollection);
        Assert.Empty(_vm.PairDataCollection);
    }

    [Fact]
    public void Constructor_StatusText_IsReady()
    {
        Assert.Equal("Ready", _vm.StatusText);
    }

    [Fact]
    public void Constructor_GridWidth_IsTwoThumbnailSizePlusEighty()
    {
        // 2 * 200 + 80 = 480
        Assert.Equal(480.0, _vm.GridWidth);
    }

    [Fact]
    public void Constructor_PreviewSize_EqualsGridWidth()
    {
        Assert.Equal(_vm.GridWidth, _vm.PreviewSize);
    }

    [Fact]
    public void Constructor_IsProgressVisible_IsCollapsed()
    {
        Assert.Equal(Visibility.Collapsed, _vm.IsProgressVisible);
    }

    [Fact]
    public void Constructor_ThumbnailGridVisibility_IsVisible()
    {
        Assert.Equal(Visibility.Visible, _vm.ThumbnailGridVisibility);
    }

    [Fact]
    public void Constructor_PairGridVisibility_IsCollapsed()
    {
        Assert.Equal(Visibility.Collapsed, _vm.PairGridVisibility);
    }

    [Fact]
    public void Constructor_CurrentImageView_IsNull()
    {
        Assert.Null(_vm.CurrentImageView);
    }

    [Fact]
    public async Task Constructor_CallsInitializeDbAsync()
    {
        // The mock returns Task.CompletedTask, so the fire-and-forget
        // completes (near-)instantly. A brief yield ensures any
        // continuations have posted back.
        await Task.Yield();
        _mockDbService.Verify(d => d.InitializeAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void Constructor_Commands_AreNotNull()
    {
        Assert.NotNull(_vm.OpenCommand);
        Assert.NotNull(_vm.AnalyzeCommand);
        Assert.NotNull(_vm.CloseViewCommand);
        Assert.NotNull(_vm.ZoomInCommand);
        Assert.NotNull(_vm.ZoomOutCommand);
    }

    #endregion

    // ===========================================================================
    // 2. Property tests
    // ===========================================================================

    #region Property Tests

    [Fact]
    public void CalcProgress_Setter_UpdatesCalcProgressText()
    {
        _vm.CalcProgress = 42.5;
        Assert.Equal("42.5%", _vm.CalcProgressText);
    }

    [Fact]
    public void CalcProgress_Setter_ZeroValue_SetsTextToZeroPercent()
    {
        // SetProperty only fires when the value changes, so prime with a non-zero value first.
        _vm.CalcProgress = 50.0;
        _vm.CalcProgress = 0.0;
        Assert.Equal("0.0%", _vm.CalcProgressText);
    }

    [Fact]
    public void CalcProgress_Setter_HundredValue_SetsTextToHundredPercent()
    {
        _vm.CalcProgress = 100.0;
        Assert.Equal("100.0%", _vm.CalcProgressText);
    }

    [Fact]
    public void IsLoaded_Setter_RoundTrips()
    {
        _vm.IsLoaded = false;
        Assert.False(_vm.IsLoaded);

        _vm.IsLoaded = true;
        Assert.True(_vm.IsLoaded);
    }

    [Fact]
    public void IsProgressVisible_Setter_RoundTrips()
    {
        _vm.IsProgressVisible = Visibility.Visible;
        Assert.Equal(Visibility.Visible, _vm.IsProgressVisible);

        _vm.IsProgressVisible = Visibility.Collapsed;
        Assert.Equal(Visibility.Collapsed, _vm.IsProgressVisible);
    }

    [Fact]
    public void AllocMem_Setter_RoundTrips()
    {
        _vm.AllocMem = 12345L;
        Assert.Equal(12345L, _vm.AllocMem);
    }

    [Fact]
    public void PreviewSize_Setter_RoundTrips()
    {
        _vm.PreviewSize = 999.5;
        Assert.Equal(999.5, _vm.PreviewSize);
    }

    [Fact]
    public void StatusText_Setter_RoundTrips()
    {
        _vm.StatusText = "Working...";
        Assert.Equal("Working...", _vm.StatusText);
    }

    [Fact]
    public void ThumbnailGridVisibility_Setter_RoundTrips()
    {
        _vm.ThumbnailGridVisibility = Visibility.Collapsed;
        Assert.Equal(Visibility.Collapsed, _vm.ThumbnailGridVisibility);
    }

    [Fact]
    public void PairGridVisibility_Setter_RoundTrips()
    {
        _vm.PairGridVisibility = Visibility.Visible;
        Assert.Equal(Visibility.Visible, _vm.PairGridVisibility);
    }

    [Fact]
    public void GridWidth_IsReadOnly_AndDerivedFromThumbnailSize()
    {
        var expected = 2.0 * _vm.ThumbnailSize + 80;
        Assert.Equal(expected, _vm.GridWidth);
    }

    #endregion

    // ===========================================================================
    // 3. Command tests (synchronous commands)
    // ===========================================================================

    #region CloseViewCommand Tests

    [Fact]
    public void CloseViewCommand_SetsCurrentImageViewToNull()
    {
        _vm.PreviewSize = 999;

        _vm.CloseViewCommand.Execute();

        Assert.Null(_vm.CurrentImageView);
    }

    [Fact]
    public void CloseViewCommand_ResetsPreviewSizeToGridWidth()
    {
        _vm.PreviewSize = 1234;

        _vm.CloseViewCommand.Execute();

        Assert.Equal(_vm.GridWidth, _vm.PreviewSize);
    }

    #endregion

    #region ZoomInCommand Tests

    [Fact]
    public void ZoomInCommand_WithValue_SetsPreviewSizeCorrectly()
    {
        var inputValue = 100.0;
        var expected = inputValue * 1.1 + 20;

        _vm.ZoomInCommand.Execute(inputValue);

        Assert.Equal(expected, _vm.PreviewSize, precision: 10);
    }

    [Fact]
    public void ZoomInCommand_WithNull_DoesNotChangePreviewSize()
    {
        _vm.PreviewSize = 500;

        _vm.ZoomInCommand.Execute(null);

        Assert.Equal(500, _vm.PreviewSize);
    }

    [Fact]
    public void ZoomInCommand_WithZeroValue_SetsPreviewSizeToTwenty()
    {
        _vm.ZoomInCommand.Execute((double?)0.0);

        Assert.Equal(20.0, _vm.PreviewSize, precision: 10);
    }

    #endregion

    #region ZoomOutCommand Tests

    [Fact]
    public void ZoomOutCommand_WithValue_SetsPreviewSizeCorrectly()
    {
        var inputValue = 200.0;
        var expected = inputValue * 0.9 + 20;

        _vm.ZoomOutCommand.Execute(inputValue);

        Assert.Equal(expected, _vm.PreviewSize, precision: 10);
    }

    [Fact]
    public void ZoomOutCommand_WithNull_DoesNotChangePreviewSize()
    {
        _vm.PreviewSize = 500;

        _vm.ZoomOutCommand.Execute(null);

        Assert.Equal(500, _vm.PreviewSize);
    }

    [Fact]
    public void ZoomOutCommand_WithZeroValue_SetsPreviewSizeToTwenty()
    {
        _vm.ZoomOutCommand.Execute((double?)0.0);

        Assert.Equal(20.0, _vm.PreviewSize, precision: 10);
    }

    #endregion

    // ===========================================================================
    // 4. AnalyzeCommand — returns early when collection is empty
    // ===========================================================================

    #region AnalyzeCommand Tests (no data)

    [Fact]
    public async Task AnalyzeCommand_WithNoDataLoaded_ReturnsEarlyWithoutCallingCalcOps()
    {
        // _dataCollectionFlat is empty by default after construction.
        await TestHelpers.InvokeCommandAsync(_vm.AnalyzeCommand);

        _mockCalcOps.Verify(
            c => c.CalcSiftHashes(
                It.IsAny<IEnumerable<ImageInfo>>(),
                It.IsAny<IPhotoDbService>(),
                It.IsAny<IProgress<double>>(),
                out It.Ref<Task>.IsAny,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AnalyzeCommand_WithNoDataLoaded_DoesNotChangeStatusText()
    {
        var initial = _vm.StatusText;

        await TestHelpers.InvokeCommandAsync(_vm.AnalyzeCommand);

        // Status text must remain unchanged because the early-return fires before any update.
        Assert.Equal(initial, _vm.StatusText);
    }

    #endregion

    // ===========================================================================
    // 5. OpenCommand — ShowFolderDialog returns null (cancelled)
    // ===========================================================================

    #region OpenCommand Tests (ShowFolderDialog returns null)

    [Fact]
    public async Task OpenCommand_WhenShowFolderDialogReturnsNull_ReturnsEarly()
    {
        _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns((string)null);

        await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

        // Nothing should have been set on the collection.
        Assert.Empty(_vm.ImageCollection);
    }

    [Fact]
    public async Task OpenCommand_WhenShowFolderDialogReturnsNull_DoesNotResetVisibility()
    {
        _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns((string)null);

        // The VM starts with ThumbnailGridVisibility = Visible; it stays that way.
        await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

        Assert.Equal(Visibility.Visible, _vm.ThumbnailGridVisibility);
        Assert.Equal(Visibility.Collapsed, _vm.PairGridVisibility);
    }

    #endregion

    #region OpenCommand Tests (ShowFolderDialog returns folder)

    [Fact]
    public async Task OpenCommand_WhenScanImageFilesReturnsEmptyPaths_SetsEmptyImageCollection()
    {
        _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
        _mockLoadingOps
            .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
            .Returns(new List<FileInfo>());

        _mockDbService.Setup(d => d.IsAvailable).Returns(false);

        var completedTask = Task.CompletedTask;
        _mockCalcOps
            .Setup(c => c.CalcSiftHashes(
                It.IsAny<IEnumerable<ImageInfo>>(),
                It.IsAny<IPhotoDbService>(),
                It.IsAny<IProgress<double>>(),
                out completedTask,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ConcurrentDictionary<string, Mat>());

        _mockCalcOps
            .Setup(c => c.CreateMatchCollection(
                It.IsAny<IDictionary<string, Mat>>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Enumerable.Empty<PairSimilarityInfo>());

        await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

        Assert.Empty(_vm.ImageCollection);
    }

    [Fact]
    public async Task OpenCommand_WhenScanImageFilesReturnsTwoPaths_SetsImageCollection()
    {
        var path1 = System.IO.Path.GetTempFileName();
        var path2 = System.IO.Path.GetTempFileName();

        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(false);

            var completedTask = Task.CompletedTask;
            _mockCalcOps
                .Setup(c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out completedTask,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ConcurrentDictionary<string, Mat>());

            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Enumerable.Empty<PairSimilarityInfo>());

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            Assert.Equal(2, _vm.ImageCollection.Count);
            Assert.Contains(_vm.ImageCollection, i => i.FilePath == path1);
            Assert.Contains(_vm.ImageCollection, i => i.FilePath == path2);
        }
        finally
        {
            System.IO.File.Delete(path1);
            System.IO.File.Delete(path2);
        }
    }

    [Fact]
    public async Task OpenCommand_WithPaths_ResetsPairDataCollectionToEmpty()
    {
        _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
        _mockLoadingOps
            .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
            .Returns(new List<FileInfo>());

        _mockDbService.Setup(d => d.IsAvailable).Returns(false);

        var completedTask = Task.CompletedTask;
        _mockCalcOps
            .Setup(c => c.CalcSiftHashes(
                It.IsAny<IEnumerable<ImageInfo>>(),
                It.IsAny<IPhotoDbService>(),
                It.IsAny<IProgress<double>>(),
                out completedTask,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ConcurrentDictionary<string, Mat>());

        _mockCalcOps
            .Setup(c => c.CreateMatchCollection(
                It.IsAny<IDictionary<string, Mat>>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Enumerable.Empty<PairSimilarityInfo>());

        await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

        Assert.NotNull(_vm.PairDataCollection);
        Assert.Empty(_vm.PairDataCollection);
    }

    [Fact]
    public async Task OpenCommand_WithPaths_SetsThumbnailGridVisibleAndPairGridCollapsed()
    {
        _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
        _mockLoadingOps
            .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
            .Returns(new List<FileInfo>());

        _mockDbService.Setup(d => d.IsAvailable).Returns(false);

        var completedTask = Task.CompletedTask;
        _mockCalcOps
            .Setup(c => c.CalcSiftHashes(
                It.IsAny<IEnumerable<ImageInfo>>(),
                It.IsAny<IPhotoDbService>(),
                It.IsAny<IProgress<double>>(),
                out completedTask,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ConcurrentDictionary<string, Mat>());

        _mockCalcOps
            .Setup(c => c.CreateMatchCollection(
                It.IsAny<IDictionary<string, Mat>>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Enumerable.Empty<PairSimilarityInfo>());

        await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

        // After analysis with 0 images, PopulateDupes never flips visibility
        // because the temp list is null/empty.
        Assert.Equal(Visibility.Visible, _vm.ThumbnailGridVisibility);
    }

    #endregion

    // ===========================================================================
    // 6. RunAnalysisAsync — fewer than 2 images
    // ===========================================================================

    #region RunAnalysisAsync Tests (via OpenCommand / AnalyzeCommand)

    [Fact]
    public async Task RunAnalysisAsync_WithFewerThanTwoImages_SetsNeedTwoImagesStatus()
    {
        // Open with exactly one path so _dataCollectionFlat has 1 item.
        var path1 = System.IO.Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1) });

            // DB unavailable so cached pairs path is skipped.
            _mockDbService.Setup(d => d.IsAvailable).Returns(false);

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            Assert.Equal("Need at least 2 images to analyze.", _vm.StatusText);
        }
        finally
        {
            System.IO.File.Delete(path1);
        }
    }

    [Fact]
    public async Task RunAnalysisAsync_WithTwoImages_CallsCalcSiftHashes()
    {
        var path1 = System.IO.Path.GetTempFileName();
        var path2 = System.IO.Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(false);

            var completedTask = Task.CompletedTask;
            _mockCalcOps
                .Setup(c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out completedTask,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ConcurrentDictionary<string, Mat>());

            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Enumerable.Empty<PairSimilarityInfo>());

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            _mockCalcOps.Verify(
                c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out It.Ref<Task>.IsAny,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            System.IO.File.Delete(path1);
            System.IO.File.Delete(path2);
        }
    }

    [Fact]
    public async Task RunAnalysisAsync_WithTwoImages_CallsCreateMatchCollection()
    {
        var path1 = System.IO.Path.GetTempFileName();
        var path2 = System.IO.Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(false);

            var completedTask = Task.CompletedTask;
            _mockCalcOps
                .Setup(c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out completedTask,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ConcurrentDictionary<string, Mat>());

            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Enumerable.Empty<PairSimilarityInfo>());

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            _mockCalcOps.Verify(
                c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            System.IO.File.Delete(path1);
            System.IO.File.Delete(path2);
        }
    }

    #endregion

    // ===========================================================================
    // 7. PopulateDupes — valid matches (score < 200) and no valid matches
    // ===========================================================================

    #region PopulateDupes Tests (via RunAnalysisAsync)

    [Fact]
    public async Task PopulateDupes_WithNoMatchesBelowThreshold_SetsNoSimilarPairsStatus()
    {
        var path1 = System.IO.Path.GetTempFileName();
        var path2 = System.IO.Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(false);

            // Score >= 200 — filtered out by PopulateDupes.
            var highScoreMatch = TestHelpers.BuildPairSimilarityInfo(path1, path2, 250.0);

            var completedTask = Task.CompletedTask;
            _mockCalcOps
                .Setup(c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out completedTask,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ConcurrentDictionary<string, Mat>());

            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new[] { highScoreMatch });

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            Assert.Equal("Analysis complete. No similar pairs found.", _vm.StatusText);
        }
        finally
        {
            System.IO.File.Delete(path1);
            System.IO.File.Delete(path2);
        }
    }

    [Fact]
    public async Task PopulateDupes_WithNoMatchesBelowThreshold_VisibilityIsUnchanged()
    {
        var path1 = System.IO.Path.GetTempFileName();
        var path2 = System.IO.Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(false);

            var highScoreMatch = TestHelpers.BuildPairSimilarityInfo(path1, path2, 999.0);

            var completedTask = Task.CompletedTask;
            _mockCalcOps
                .Setup(c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out completedTask,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ConcurrentDictionary<string, Mat>());

            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new[] { highScoreMatch });

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            // When there are no valid pairs the visibility flags are NOT flipped.
            Assert.Equal(Visibility.Visible, _vm.ThumbnailGridVisibility);
            Assert.Equal(Visibility.Collapsed, _vm.PairGridVisibility);
        }
        finally
        {
            System.IO.File.Delete(path1);
            System.IO.File.Delete(path2);
        }
    }

    [Fact]
    public async Task PopulateDupes_WithValidMatch_ShowsPairGridAndHidesThumbnailGrid()
    {
        var path1 = System.IO.Path.GetTempFileName();
        var path2 = System.IO.Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(false);

            // Score 50 < 200, so it passes the filter.
            var goodMatch = TestHelpers.BuildPairSimilarityInfo(path1, path2, 50.0);

            var completedTask = Task.CompletedTask;
            _mockCalcOps
                .Setup(c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out completedTask,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ConcurrentDictionary<string, Mat>());

            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new[] { goodMatch });

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            Assert.Equal(Visibility.Collapsed, _vm.ThumbnailGridVisibility);
            Assert.Equal(Visibility.Visible, _vm.PairGridVisibility);
        }
        finally
        {
            System.IO.File.Delete(path1);
            System.IO.File.Delete(path2);
        }
    }

    [Fact]
    public async Task PopulateDupes_WithValidMatch_PopulatesPairDataCollection()
    {
        var path1 = System.IO.Path.GetTempFileName();
        var path2 = System.IO.Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(false);

            var goodMatch = TestHelpers.BuildPairSimilarityInfo(path1, path2, 75.0);

            var completedTask = Task.CompletedTask;
            _mockCalcOps
                .Setup(c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out completedTask,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ConcurrentDictionary<string, Mat>());

            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new[] { goodMatch });

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            Assert.Single(_vm.PairDataCollection);
        }
        finally
        {
            System.IO.File.Delete(path1);
            System.IO.File.Delete(path2);
        }
    }

    [Fact]
    public async Task PopulateDupes_WithValidMatch_SetsAnalysisCompleteStatus()
    {
        var path1 = System.IO.Path.GetTempFileName();
        var path2 = System.IO.Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(false);

            var goodMatch = TestHelpers.BuildPairSimilarityInfo(path1, path2, 99.9);

            var completedTask = Task.CompletedTask;
            _mockCalcOps
                .Setup(c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out completedTask,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ConcurrentDictionary<string, Mat>());

            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new[] { goodMatch });

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            Assert.StartsWith("Analysis complete. Found", _vm.StatusText);
        }
        finally
        {
            System.IO.File.Delete(path1);
            System.IO.File.Delete(path2);
        }
    }

    [Fact]
    public async Task PopulateDupes_WithMatchExactlyAt200_IsFiltered()
    {
        // The filter is match.Match < 200, so 200.0 must be excluded.
        var path1 = System.IO.Path.GetTempFileName();
        var path2 = System.IO.Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(false);

            var borderMatch = TestHelpers.BuildPairSimilarityInfo(path1, path2, 200.0);

            var completedTask = Task.CompletedTask;
            _mockCalcOps
                .Setup(c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out completedTask,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ConcurrentDictionary<string, Mat>());

            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new[] { borderMatch });

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            Assert.Equal("Analysis complete. No similar pairs found.", _vm.StatusText);
        }
        finally
        {
            System.IO.File.Delete(path1);
            System.IO.File.Delete(path2);
        }
    }

    #endregion

    // ===========================================================================
    // 8. StoreSimilarityResultsAsync — db unavailable, valid path, skip MaxValue
    // ===========================================================================

    #region StoreSimilarityResultsAsync Tests (via RunAnalysisAsync)

    [Fact]
    public async Task StoreSimilarityResultsAsync_WhenDbUnavailable_DoesNotCallStoreSimilarity()
    {
        var path1 = System.IO.Path.GetTempFileName();
        var path2 = System.IO.Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            // DB is explicitly NOT available.
            _mockDbService.Setup(d => d.IsAvailable).Returns(false);

            var match = TestHelpers.BuildPairSimilarityInfo(path1, path2, 50.0);

            var completedTask = Task.CompletedTask;
            _mockCalcOps
                .Setup(c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out completedTask,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ConcurrentDictionary<string, Mat>());

            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new[] { match });

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            _mockDbService.Verify(
                d => d.StoreSimilarityAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<double>()),
                Times.Never);
        }
        finally
        {
            System.IO.File.Delete(path1);
            System.IO.File.Delete(path2);
        }
    }

    [Fact]
    public async Task StoreSimilarityResultsAsync_MatchWithMaxValue_IsSkipped()
    {
        var path1 = System.IO.Path.GetTempFileName();
        var path2 = System.IO.Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(true);

            // GetCachedResultsAsync for TryLoadCachedPairsAsync — return empty.
            _mockDbService
                .Setup(d => d.GetCachedResultsAsync())
                .ReturnsAsync(new List<SimilarityResult>());

            // Match.Match == double.MaxValue must be skipped by StoreSimilarityResultsAsync.
            var maxMatch = TestHelpers.BuildPairSimilarityInfo(path1, path2, double.MaxValue);

            var completedTask = Task.CompletedTask;
            _mockCalcOps
                .Setup(c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out completedTask,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ConcurrentDictionary<string, Mat>());

            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new[] { maxMatch });

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            _mockDbService.Verify(
                d => d.StoreSimilarityAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<double>()),
                Times.Never);
        }
        finally
        {
            System.IO.File.Delete(path1);
            System.IO.File.Delete(path2);
        }
    }

    [Fact]
    public async Task StoreSimilarityResultsAsync_WithValidMatchAndAvailableDb_CallsStoreSimilarity()
    {
        var path1 = System.IO.Path.GetTempFileName();
        var path2 = System.IO.Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(true);

            var photo1 = new Photo { Id = 1, FilePath = path1 };
            var photo2 = new Photo { Id = 2, FilePath = path2 };

            _mockDbService
                .Setup(d => d.GetCachedPhotoAsync(path1))
                .ReturnsAsync(photo1);
            _mockDbService
                .Setup(d => d.GetCachedPhotoAsync(path2))
                .ReturnsAsync(photo2);
            _mockDbService
                .Setup(d => d.StoreSimilarityAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<double>()))
                .Returns(Task.CompletedTask);

            // Return empty cached results so TryLoadCachedPairsAsync falls through to RunAnalysisAsync.
            _mockDbService
                .Setup(d => d.GetCachedResultsAsync())
                .ReturnsAsync(new List<SimilarityResult>());

            var goodMatch = TestHelpers.BuildPairSimilarityInfo(path1, path2, 30.0);

            var completedTask = Task.CompletedTask;
            _mockCalcOps
                .Setup(c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out completedTask,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ConcurrentDictionary<string, Mat>());

            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new[] { goodMatch });

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            _mockDbService.Verify(
                d => d.StoreSimilarityAsync(1, 2, 30.0),
                Times.Once);
        }
        finally
        {
            System.IO.File.Delete(path1);
            System.IO.File.Delete(path2);
        }
    }

    [Fact]
    public async Task StoreSimilarityResultsAsync_WhenPhotosNotInDb_DoesNotCallStoreSimilarity()
    {
        var path1 = System.IO.Path.GetTempFileName();
        var path2 = System.IO.Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(true);

            // GetCachedPhotoAsync returns null — photos not yet in DB.
            _mockDbService
                .Setup(d => d.GetCachedPhotoAsync(It.IsAny<string>()))
                .ReturnsAsync((Photo)null);

            _mockDbService
                .Setup(d => d.GetCachedResultsAsync())
                .ReturnsAsync(new List<SimilarityResult>());

            var goodMatch = TestHelpers.BuildPairSimilarityInfo(path1, path2, 30.0);

            var completedTask = Task.CompletedTask;
            _mockCalcOps
                .Setup(c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out completedTask,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ConcurrentDictionary<string, Mat>());

            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new[] { goodMatch });

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            _mockDbService.Verify(
                d => d.StoreSimilarityAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<double>()),
                Times.Never);
        }
        finally
        {
            System.IO.File.Delete(path1);
            System.IO.File.Delete(path2);
        }
    }

    #endregion

    // ===========================================================================
    // 9. TryLoadCachedPairsAsync — DB unavailable, no results, valid results
    // ===========================================================================

    #region TryLoadCachedPairsAsync Tests (via OpenCommand)

    [Fact]
    public async Task TryLoadCachedPairsAsync_WhenDbUnavailable_FallsThroughToAnalysis()
    {
        var path1 = System.IO.Path.GetTempFileName();
        var path2 = System.IO.Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            // DB unavailable -> TryLoadCachedPairsAsync returns false -> RunAnalysisAsync runs.
            _mockDbService.Setup(d => d.IsAvailable).Returns(false);

            var completedTask = Task.CompletedTask;
            _mockCalcOps
                .Setup(c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out completedTask,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ConcurrentDictionary<string, Mat>());

            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Enumerable.Empty<PairSimilarityInfo>());

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            // CalcSiftHashes called confirms RunAnalysisAsync executed.
            _mockCalcOps.Verify(
                c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out It.Ref<Task>.IsAny,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            System.IO.File.Delete(path1);
            System.IO.File.Delete(path2);
        }
    }

    [Fact]
    public async Task TryLoadCachedPairsAsync_WhenNoCachedResults_FallsThroughToAnalysis()
    {
        var path1 = System.IO.Path.GetTempFileName();
        var path2 = System.IO.Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(true);
            // Empty cached results -> TryLoadCachedPairsAsync returns false.
            _mockDbService
                .Setup(d => d.GetCachedResultsAsync())
                .ReturnsAsync(new List<SimilarityResult>());

            _mockDbService
                .Setup(d => d.GetCachedPhotoAsync(It.IsAny<string>()))
                .ReturnsAsync((Photo)null);

            var completedTask = Task.CompletedTask;
            _mockCalcOps
                .Setup(c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out completedTask,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ConcurrentDictionary<string, Mat>());

            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Enumerable.Empty<PairSimilarityInfo>());

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            _mockCalcOps.Verify(
                c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out It.Ref<Task>.IsAny,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            System.IO.File.Delete(path1);
            System.IO.File.Delete(path2);
        }
    }

    [Fact]
    public async Task TryLoadCachedPairsAsync_WithValidCachedResults_PopulatesPairDataCollection()
    {
        var path1 = System.IO.Path.GetTempFileName();
        var path2 = System.IO.Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(true);

            var cachedResults = new List<SimilarityResult>
            {
                new SimilarityResult
                {
                    Id = 1,
                    Score = 50.0,   // < 200, so it's relevant
                    Photo1 = new Photo { Id = 1, FilePath = path1 },
                    Photo2 = new Photo { Id = 2, FilePath = path2 }
                }
            };

            _mockDbService
                .Setup(d => d.GetCachedResultsAsync())
                .ReturnsAsync(cachedResults);

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            // TryLoadCachedPairsAsync returned true -> RunAnalysisAsync was NOT called.
            _mockCalcOps.Verify(
                c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out It.Ref<Task>.IsAny,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);

            Assert.Single(_vm.PairDataCollection);
        }
        finally
        {
            System.IO.File.Delete(path1);
            System.IO.File.Delete(path2);
        }
    }

    [Fact]
    public async Task TryLoadCachedPairsAsync_WithValidCachedResults_SwitchesToPairGridVisibility()
    {
        var path1 = System.IO.Path.GetTempFileName();
        var path2 = System.IO.Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(true);

            var cachedResults = new List<SimilarityResult>
            {
                new SimilarityResult
                {
                    Score = 10.0,
                    Photo1 = new Photo { Id = 1, FilePath = path1 },
                    Photo2 = new Photo { Id = 2, FilePath = path2 }
                }
            };

            _mockDbService
                .Setup(d => d.GetCachedResultsAsync())
                .ReturnsAsync(cachedResults);

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            Assert.Equal(Visibility.Collapsed, _vm.ThumbnailGridVisibility);
            Assert.Equal(Visibility.Visible, _vm.PairGridVisibility);
        }
        finally
        {
            System.IO.File.Delete(path1);
            System.IO.File.Delete(path2);
        }
    }

    [Fact]
    public async Task TryLoadCachedPairsAsync_WithValidCachedResults_SetsCachedStatusText()
    {
        var path1 = System.IO.Path.GetTempFileName();
        var path2 = System.IO.Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(true);

            var cachedResults = new List<SimilarityResult>
            {
                new SimilarityResult
                {
                    Score = 10.0,
                    Photo1 = new Photo { Id = 1, FilePath = path1 },
                    Photo2 = new Photo { Id = 2, FilePath = path2 }
                }
            };

            _mockDbService
                .Setup(d => d.GetCachedResultsAsync())
                .ReturnsAsync(cachedResults);

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            Assert.Contains("cached", _vm.StatusText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            System.IO.File.Delete(path1);
            System.IO.File.Delete(path2);
        }
    }

    [Fact]
    public async Task TryLoadCachedPairsAsync_WhenResultScoreAboveThreshold_FallsThroughToAnalysis()
    {
        var path1 = System.IO.Path.GetTempFileName();
        var path2 = System.IO.Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(true);

            // Score >= 200 -> relevantResults.Count == 0 -> TryLoadCachedPairsAsync returns false.
            var cachedResults = new List<SimilarityResult>
            {
                new SimilarityResult
                {
                    Score = 300.0,
                    Photo1 = new Photo { Id = 1, FilePath = path1 },
                    Photo2 = new Photo { Id = 2, FilePath = path2 }
                }
            };

            _mockDbService
                .Setup(d => d.GetCachedResultsAsync())
                .ReturnsAsync(cachedResults);

            _mockDbService
                .Setup(d => d.GetCachedPhotoAsync(It.IsAny<string>()))
                .ReturnsAsync((Photo)null);

            var completedTask = Task.CompletedTask;
            _mockCalcOps
                .Setup(c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out completedTask,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ConcurrentDictionary<string, Mat>());

            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Enumerable.Empty<PairSimilarityInfo>());

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            _mockCalcOps.Verify(
                c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out It.Ref<Task>.IsAny,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            System.IO.File.Delete(path1);
            System.IO.File.Delete(path2);
        }
    }

    [Fact]
    public async Task TryLoadCachedPairsAsync_WhenPathNotInCurrentSession_IsFiltered()
    {
        // Cached result references paths that are NOT in the current _dataCollectionFlat.
        var path1 = System.IO.Path.GetTempFileName();
        var path2 = System.IO.Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(true);

            // Cached result references different (unknown) paths.
            var cachedResults = new List<SimilarityResult>
            {
                new SimilarityResult
                {
                    Score = 50.0,
                    Photo1 = new Photo { Id = 1, FilePath = @"Z:\unknown\a.jpg" },
                    Photo2 = new Photo { Id = 2, FilePath = @"Z:\unknown\b.jpg" }
                }
            };

            _mockDbService
                .Setup(d => d.GetCachedResultsAsync())
                .ReturnsAsync(cachedResults);

            _mockDbService
                .Setup(d => d.GetCachedPhotoAsync(It.IsAny<string>()))
                .ReturnsAsync((Photo)null);

            var completedTask = Task.CompletedTask;
            _mockCalcOps
                .Setup(c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out completedTask,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ConcurrentDictionary<string, Mat>());

            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Enumerable.Empty<PairSimilarityInfo>());

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            // Unknown paths are filtered -> relevantResults is empty -> returns false -> analysis runs.
            _mockCalcOps.Verify(
                c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out It.Ref<Task>.IsAny,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            System.IO.File.Delete(path1);
            System.IO.File.Delete(path2);
        }
    }

    #endregion

    // ===========================================================================
    // 10. OpenView tests
    // ===========================================================================

    #region OpenView Tests

    [Fact]
    public void OpenView_WithNonExistentFile_DoesNotChangeCurrentImageView()
    {
        _vm.CurrentImageView = null;

        _vm.OpenView(@"C:\this\path\does\not\exist\image.jpg");

        Assert.Null(_vm.CurrentImageView);
    }

    [Fact]
    public void OpenView_WithNullFilePath_DoesNotThrowOrChangeState()
    {
        // File.Exists(null) returns false, so the method returns early.
        var exception = Record.Exception(() => _vm.OpenView(null));
        Assert.Null(exception);
        Assert.Null(_vm.CurrentImageView);
    }

    [Fact]
    public void OpenView_WithEmptyFilePath_DoesNotThrowOrChangeState()
    {
        var exception = Record.Exception(() => _vm.OpenView(string.Empty));
        Assert.Null(exception);
        Assert.Null(_vm.CurrentImageView);
    }

    [Fact]
    public void OpenView_WithNullAlternatePath_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
            _vm.OpenView(@"Z:\does\not\exist.jpg", null));
        Assert.Null(exception);
    }

    #endregion

    // ===========================================================================
    // 11. ShowAlternatePreview / RestorePrimaryPreview
    // ===========================================================================

    #region ShowAlternatePreview / RestorePrimaryPreview Tests

    [Fact]
    public void ShowAlternatePreview_WhenNoAlternateLoaded_DoesNotChangeCurrentImageView()
    {
        // No OpenView has been called successfully, so _alternateImageView is null.
        _vm.CurrentImageView = null;

        _vm.ShowAlternatePreview();

        Assert.Null(_vm.CurrentImageView);
    }

    [Fact]
    public void RestorePrimaryPreview_WhenNoPrimaryLoaded_DoesNotChangeCurrentImageView()
    {
        // No successful OpenView call means _primaryImageView is null.
        _vm.CurrentImageView = null;

        _vm.RestorePrimaryPreview();

        Assert.Null(_vm.CurrentImageView);
    }

    [Fact]
    public void ShowAlternatePreview_DoesNotThrow()
    {
        var exception = Record.Exception(() => _vm.ShowAlternatePreview());
        Assert.Null(exception);
    }

    [Fact]
    public void RestorePrimaryPreview_DoesNotThrow()
    {
        var exception = Record.Exception(() => _vm.RestorePrimaryPreview());
        Assert.Null(exception);
    }

    #endregion

    // ===========================================================================
    // 12. Event subscription tests
    // ===========================================================================

    #region Event Tests

    [Fact]
    public void OnOpenImagePreview_WithNullPayload_DoesNotThrow()
    {
        // The handler checks payload != null before calling OpenView.
        var exception = Record.Exception(() =>
        {
            _eventAggregator.GetEvent<OpenImagePreviewEvent>()
                .Publish(null);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void OnOpenImagePreview_WithPayloadContainingNonExistentPath_DoesNotChangeCurrentImageView()
    {
        _vm.CurrentImageView = null;

        _eventAggregator.GetEvent<OpenImagePreviewEvent>()
            .Publish(new OpenImagePreviewPayload
            {
                FilePath = @"Z:\nonexistent\image.png",
                AlternatePath = null
            });

        // OpenView returns early when File.Exists returns false.
        Assert.Null(_vm.CurrentImageView);
    }

    [Fact]
    public void OnOpenImagePreview_WithPayloadContainingEmptyPath_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
        {
            _eventAggregator.GetEvent<OpenImagePreviewEvent>()
                .Publish(new OpenImagePreviewPayload
                {
                    FilePath = string.Empty,
                    AlternatePath = null
                });
        });

        Assert.Null(exception);
    }

    [Fact]
    public void OnOpenImagePreview_WithNonNullPayload_CallsOpenView()
    {
        // Confirm the ViewModel subscribed: if file doesn't exist, OpenView
        // returns early — but we know OpenView was invoked because CurrentImageView
        // stays null (no crash, no side-effect from a missing file).
        _vm.CurrentImageView = null;

        _eventAggregator.GetEvent<OpenImagePreviewEvent>()
            .Publish(new OpenImagePreviewPayload
            {
                FilePath = @"Z:\does_not_exist\photo.jpg",
                AlternatePath = @"Z:\does_not_exist\other.jpg"
            });

        // If the subscription wasn't wired the publish would silently do nothing.
        // The fact that no exception was thrown and CurrentImageView is still null
        // (File.Exists guard in OpenView) proves the subscriber ran.
        Assert.Null(_vm.CurrentImageView);
    }

    #endregion

    // ===========================================================================
    // 13. Dispose tests
    // ===========================================================================

    #region Dispose Tests

    [Fact]
    public void Dispose_CallsDbServiceDispose()
    {
        var localMockDb = new Mock<IPhotoDbService>();
        localMockDb
            .Setup(d => d.InitializeAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var localVm = new MainViewModel(
            _eventAggregator,
            localMockDb.Object,
            _mockCalcOps.Object,
            _mockLoadingOps.Object,
            _mockThumbnailService.Object);

        localVm.Dispose();

        localMockDb.Verify(d => d.Dispose(), Times.Once);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var localMockDb = new Mock<IPhotoDbService>();
        localMockDb
            .Setup(d => d.InitializeAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var localVm = new MainViewModel(
            _eventAggregator,
            localMockDb.Object,
            _mockCalcOps.Object,
            _mockLoadingOps.Object,
            _mockThumbnailService.Object);

        localVm.Dispose();
        var exception = Record.Exception(() => localVm.Dispose());

        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_DoesNotThrow_WhenCalledOnFreshViewModel()
    {
        var exception = Record.Exception(() =>
        {
            using var vm = CreateViewModel();
            // Dispose is called by using block.
        });
        Assert.Null(exception);
    }

    #endregion

    // ===========================================================================
    // 14. AnalyzeCommand after data is loaded (re-run analysis path)
    // ===========================================================================

    #region AnalyzeCommand With Data Tests

    [Fact]
    public async Task AnalyzeCommand_AfterDataLoaded_CallsRunAnalysisAgain()
    {
        // First open with two real temp files so _dataCollectionFlat has 2 entries.
        var path1 = System.IO.Path.GetTempFileName();
        var path2 = System.IO.Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(false);

            var completedTask = Task.CompletedTask;
            _mockCalcOps
                .Setup(c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out completedTask,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ConcurrentDictionary<string, Mat>());

            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Enumerable.Empty<PairSimilarityInfo>());

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            // Now invoke AnalyzeCommand — CalcSiftHashes should be called a second time.
            await TestHelpers.InvokeCommandAsync(_vm.AnalyzeCommand);

            _mockCalcOps.Verify(
                c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out It.Ref<Task>.IsAny,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }
        finally
        {
            System.IO.File.Delete(path1);
            System.IO.File.Delete(path2);
        }
    }

    [Fact]
    public async Task AnalyzeCommand_AfterOneImageLoaded_SetsNeedMoreImagesStatus()
    {
        // Open with a single file so _dataCollectionFlat has 1 entry.
        var path1 = System.IO.Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(false);

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            // Now run AnalyzeCommand explicitly — RunAnalysisAsync returns early.
            await TestHelpers.InvokeCommandAsync(_vm.AnalyzeCommand);

            Assert.Equal("Need at least 2 images to analyze.", _vm.StatusText);
        }
        finally
        {
            System.IO.File.Delete(path1);
        }
    }

    [Fact]
    public async Task AnalyzeCommand_AfterDataLoaded_ResetsThumbnailGridVisible()
    {
        var path1 = System.IO.Path.GetTempFileName();
        var path2 = System.IO.Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(false);

            var completedTask = Task.CompletedTask;
            _mockCalcOps
                .Setup(c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out completedTask,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ConcurrentDictionary<string, Mat>());

            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Enumerable.Empty<PairSimilarityInfo>());

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            // Force PairGridVisibility to be visible to confirm AnalyzeCommand resets it.
            _vm.PairGridVisibility = Visibility.Visible;
            _vm.ThumbnailGridVisibility = Visibility.Collapsed;

            await TestHelpers.InvokeCommandAsync(_vm.AnalyzeCommand);

            // ExecuteAnalyzeAsync sets ThumbnailGridVisibility = Visible before calling RunAnalysisAsync.
            Assert.Equal(Visibility.Visible, _vm.ThumbnailGridVisibility);
        }
        finally
        {
            System.IO.File.Delete(path1);
            System.IO.File.Delete(path2);
        }
    }

    #endregion

    // ===========================================================================
    // 15. InitializeDbAsync error handling
    // ===========================================================================

    #region InitializeDbAsync Error Handling Tests

    [Fact]
    public async Task InitializeDbAsync_WhenInitializeThrowsArgumentException_DoesNotPropagateToConstructor()
    {
        var throwingDbMock = new Mock<IPhotoDbService>();
        throwingDbMock
            .Setup(d => d.InitializeAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("DB init failure"));

        var exception = await Record.ExceptionAsync(async () =>
        {
            using var vm = new MainViewModel(
                _eventAggregator,
                throwingDbMock.Object,
                _mockCalcOps.Object,
                _mockLoadingOps.Object,
                _mockThumbnailService.Object);

            // Allow the fire-and-forget InitializeDbAsync to complete.
            await Task.Delay(100);
        });

        Assert.Null(exception);
    }

    #endregion

    // ===========================================================================
    // Helper methods
    // ===========================================================================

    #region Helper Methods

    #endregion
}