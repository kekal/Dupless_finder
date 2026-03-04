using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dupples_finder_UI.Data.Entities;
using Dupples_finder_UI.DTO;
using Dupples_finder_UI.Modules.ViewModels;
using Dupples_finder_UI.Services.Interfaces;
using Moq;
using OpenCvSharp;
using Prism.Events;
using Xunit;

namespace Dupless_finder_Tests;

/// <summary>
/// Additional ViewModel-level tests for MainViewModel targeting specific uncovered code paths.
/// Uses the same setup pattern as MainViewModelTests (same mocks, same constructor pattern).
/// </summary>
public class MainViewModelCoverageGapTests : IDisposable
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

    public MainViewModelCoverageGapTests()
    {
        _mockDbService = new Mock<IPhotoDbService>();
        _mockCalcOps = new Mock<ICalcOperations>();
        _mockLoadingOps = new Mock<ILoadingOperations>();
        _mockThumbnailService = new Mock<IThumbnailService>();

        // InitializeDbAsync is fire-and-forget; ensure it completes instantly by default.
        _mockDbService
            .Setup(d => d.InitializeAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Prism requires a SynchronizationContext when subscribing with ThreadOption.UIThread.
        if (SynchronizationContext.Current == null)
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
        }

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
    // 1. CalcProgress getter — reads back the backing field value
    // ===========================================================================

    [Fact]
    public void CalcProgress_Getter_ReturnsSetValue()
    {
        // The getter path (_calcProgress field read) is distinct from the setter logic.
        // Setting via the property goes through SetProperty; the getter simply returns the field.
        _vm.CalcProgress = 73.2;
        var retrieved = _vm.CalcProgress;
        Assert.Equal(73.2, retrieved, precision: 10);
    }

    [Fact]
    public void CalcProgress_Getter_ReturnsDefaultZeroBeforeAnySet()
    {
        // Freshly constructed ViewModel — backing field is default(double) = 0.0.
        var retrieved = _vm.CalcProgress;
        Assert.Equal(0.0, retrieved, precision: 10);
    }

    [Fact]
    public void CalcProgress_Setter_WhenSameValueSet_DoesNotUpdateCalcProgressText()
    {
        // First set triggers the CalcProgressText assignment.
        _vm.CalcProgress = 55.0;
        Assert.Equal("55.0%", _vm.CalcProgressText);

        // Use reflection to inject a marker into _calcProgressText.
        var textField = typeof(MainViewModel)
            .GetField("_calcProgressText", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(textField);

        textField.SetValue(_vm, "MARKER");
        Assert.Equal("MARKER", _vm.CalcProgressText);

        // Set CalcProgress to the SAME value (55.0).
        // SetProperty returns false → setter body does NOT execute →
        // _calcProgressText stays "MARKER".
        _vm.CalcProgress = 55.0;

        Assert.Equal("MARKER", _vm.CalcProgressText);
    }

    // ===========================================================================
    // 2. RunAnalysisAsync IProgress<double> callbacks invoke CalcProgress setter
    //    (calcProgress and matchProgress lambdas — lines 381-384 and 394-397)
    // ===========================================================================

    [Fact]
    public async Task RunAnalysisAsync_CalcProgressCallback_UpdatesCalcProgress()
    {
        // Exercise the real production Progress<double> lambda in RunAnalysisAsync
        // by invoking OpenCommand and capturing the IProgress via mock callback.
        var path1 = Path.GetTempFileName();
        var path2 = Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(false);

            IProgress<double> capturedCalcProgress = null;
            var completedTask = Task.CompletedTask;
            _mockCalcOps
                .Setup(c => c.CalcSiftHashes(
                    It.IsAny<IEnumerable<ImageInfo>>(),
                    It.IsAny<IPhotoDbService>(),
                    It.IsAny<IProgress<double>>(),
                    out completedTask,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Callback(new InvocationAction(invocation =>
                {
                    capturedCalcProgress = invocation.Arguments[2] as IProgress<double>;
                }))
                .Returns(new ConcurrentDictionary<string, Mat>());

            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Enumerable.Empty<PairSimilarityInfo>());

            var previousCtx = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new SynchronousSyncContext());
            try
            {
                await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);
                // The production code's Progress<double> lambda sets CalcProgress.
                Assert.NotNull(capturedCalcProgress);
                capturedCalcProgress.Report(42.0);
                Assert.Equal(42.0, _vm.CalcProgress, precision: 10);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousCtx);
            }
        }
        finally
        {
            try { File.Delete(path1); } catch { }
            try { File.Delete(path2); } catch { }
        }
    }

    [Fact]
    public async Task RunAnalysisAsync_MatchProgressLambda_WhenInvoked_UpdatesCalcProgress()
    {
        // Exercise the real production matchProgress lambda by invoking OpenCommand
        // and capturing IProgress from CreateMatchCollection mock callback.
        var path1 = Path.GetTempFileName();
        var path2 = Path.GetTempFileName();
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

            IProgress<double> capturedMatchProgress = null;
            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IDictionary<string, Mat>, IProgress<double>, CancellationToken>((dict, progress, ct) =>
                {
                    capturedMatchProgress = progress;
                })
                .Returns(Enumerable.Empty<PairSimilarityInfo>());

            var previousCtx = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new SynchronousSyncContext());
            try
            {
                await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);
                Assert.NotNull(capturedMatchProgress);
                capturedMatchProgress.Report(77.5);
                Assert.Equal(77.5, _vm.CalcProgress, precision: 10);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousCtx);
            }
        }
        finally
        {
            try { File.Delete(path1); } catch { }
            try { File.Delete(path2); } catch { }
        }
    }

    [Fact]
    public async Task RunAnalysisAsync_WithTwoImages_CreateMatchCollectionReceivesProgressCallback()
    {
        // Verify that CreateMatchCollection is called with a non-null IProgress<double>
        // (i.e. the matchProgress object is wired up correctly). We capture it in the
        // Callback and confirm it is not null. We use AnalyzeCommand after loading data
        // via reflection so no file I/O race is involved.
        var path1 = Path.GetTempFileName();
        var path2 = Path.GetTempFileName();
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

            IProgress<double> capturedProgress = null;
            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IDictionary<string, Mat>, IProgress<double>, CancellationToken>((dict, progress, ct) =>
                {
                    capturedProgress = progress;
                })
                .Returns(Enumerable.Empty<PairSimilarityInfo>());

            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            // Confirm a non-null IProgress<double> was passed (the matchProgress lambda).
            Assert.NotNull(capturedProgress);
        }
        finally
        {
            File.Delete(path1);
            File.Delete(path2);
        }
    }

    [Fact]
    public void RunAnalysisAsync_CalcProgressLambda_WhenInvoked_UpdatesCalcProgressText()
    {
        // Directly simulate what the calcProgress lambda body does (lines 381-384):
        //   CalcProgress = value;
        // Verify that setting CalcProgress also updates CalcProgressText (the setter logic
        // at lines 131-134). This is a pure unit test with no async complexity.
        var previousCtx = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new SynchronousSyncContext());
        try
        {
            // Use IProgress<double> exactly as RunAnalysisAsync constructs calcProgress.
            IProgress<double> calcProgressObj = new Progress<double>(value => _vm.CalcProgress = value);
            calcProgressObj.Report(33.3);

            // SynchronousSyncContext makes Report() invoke the callback synchronously.
            // CalcProgress setter fires CalcProgressText = "33.3%".
            Assert.Equal(33.3, _vm.CalcProgress, precision: 10);
            Assert.Equal("33.3%", _vm.CalcProgressText);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousCtx);
        }
    }

    // ===========================================================================
    // 3. StoreSimilarityResultsAsync — exception catch path (lines 436-439)
    //    GetCachedPhotoAsync throws -> catch block logs via Trace.WriteLine
    //    and execution continues without propagating the exception.
    // ===========================================================================

    [Fact]
    public async Task StoreSimilarityResultsAsync_WhenGetCachedPhotoThrows_DoesNotPropagate()
    {
        var path1 = Path.GetTempFileName();
        var path2 = Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            // DB is available so StoreSimilarityResultsAsync body is entered.
            _mockDbService.Setup(d => d.IsAvailable).Returns(true);

            // GetCachedResultsAsync returns empty so TryLoadCachedPairsAsync returns false.
            _mockDbService
                .Setup(d => d.GetCachedResultsAsync())
                .ReturnsAsync(new List<SimilarityResult>());

            // GetCachedPhotoAsync throws — the catch block must swallow it.
            _mockDbService
                .Setup(d => d.GetCachedPhotoAsync(It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("simulated DB read error"));

            // Return a match with score < double.MaxValue so it isn't skipped.
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

            // Should complete without throwing — the catch block swallows the exception.
            var exception = await Record.ExceptionAsync(
                () => TestHelpers.InvokeCommandAsync(_vm.OpenCommand));

            Assert.Null(exception);
        }
        finally
        {
            File.Delete(path1);
            File.Delete(path2);
        }
    }

    [Fact]
    public async Task StoreSimilarityResultsAsync_WhenGetCachedPhotoThrows_StillCallsPopulateDupes()
    {
        // Even when the DB call throws for every match, PopulateDupes must still run
        // (execution continues after the catch block).
        var path1 = Path.GetTempFileName();
        var path2 = Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(true);

            _mockDbService
                .Setup(d => d.GetCachedResultsAsync())
                .ReturnsAsync(new List<SimilarityResult>());

            _mockDbService
                .Setup(d => d.GetCachedPhotoAsync(It.IsAny<string>()))
                .ThrowsAsync(new Exception("DB error"));

            // Match score < 200 so it passes PopulateDupes filter.
            var goodMatch = TestHelpers.BuildPairSimilarityInfo(path1, path2, 10.0);

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

            // PopulateDupes ran and found the match (score 10 < 200), so StatusText
            // should contain "Analysis complete. Found".
            Assert.StartsWith("Analysis complete. Found", _vm.StatusText);
        }
        finally
        {
            File.Delete(path1);
            File.Delete(path2);
        }
    }

    // ===========================================================================
    // 4. StoreSimilarityResultsAsync — double.MaxValue skip (lines 422-425)
    //    This is complementary to the existing test: verify the code path
    //    where ONLY a MaxValue match exists and that StoreSimilarity is never called.
    //    (The existing test already covers the skip; this test exercises a different
    //     variant where there is no DB-available guard short-circuit at all.)
    // ===========================================================================

    [Fact]
    public async Task StoreSimilarityResultsAsync_MatchMaxValue_IsSkippedAndDoesNotCallGetCachedPhoto()
    {
        var path1 = Path.GetTempFileName();
        var path2 = Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(true);

            _mockDbService
                .Setup(d => d.GetCachedResultsAsync())
                .ReturnsAsync(new List<SimilarityResult>());

            // Match with double.MaxValue — the continue statement skips it.
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

            // The continue fires before GetCachedPhotoAsync is called.
            _mockDbService.Verify(
                d => d.GetCachedPhotoAsync(It.IsAny<string>()),
                Times.Never);
        }
        finally
        {
            File.Delete(path1);
            File.Delete(path2);
        }
    }

    // ===========================================================================
    // 5. TryLoadCachedPairsAsync — exception catch (lines 502-506)
    //    GetCachedResultsAsync throws -> catch block -> return false -> analysis runs.
    // ===========================================================================

    [Fact]
    public async Task TryLoadCachedPairsAsync_WhenGetCachedResultsThrows_DoesNotPropagate()
    {
        var path1 = Path.GetTempFileName();
        var path2 = Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(true);

            // Throw inside the try block of TryLoadCachedPairsAsync.
            _mockDbService
                .Setup(d => d.GetCachedResultsAsync())
                .ThrowsAsync(new InvalidOperationException("simulated cache read failure"));

            // DB also unavailable for store path — prevent secondary throws.
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

            var exception = await Record.ExceptionAsync(
                () => TestHelpers.InvokeCommandAsync(_vm.OpenCommand));

            Assert.Null(exception);
        }
        finally
        {
            File.Delete(path1);
            File.Delete(path2);
        }
    }

    [Fact]
    public async Task TryLoadCachedPairsAsync_WhenGetCachedResultsThrows_FallsThroughToAnalysis()
    {
        // The catch block returns false, so RunAnalysisAsync must execute.
        var path1 = Path.GetTempFileName();
        var path2 = Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(true);

            _mockDbService
                .Setup(d => d.GetCachedResultsAsync())
                .ThrowsAsync(new Exception("DB read error"));

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

            // Confirm RunAnalysisAsync was executed (CalcSiftHashes was called).
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
            File.Delete(path1);
            File.Delete(path2);
        }
    }

    // ===========================================================================
    // 6a. TryLoadCachedPairsAsync — cachedResults exist but none match currentPaths
    //     (relevantResults.Count == 0 -> return false, lines 475-478)
    // ===========================================================================

    [Fact]
    public async Task TryLoadCachedPairsAsync_WhenCachedPathsNotInCurrentSession_RelevantResultsEmpty_ReturnsFalse()
    {
        // Cached results reference paths that do not appear in _dataCollectionFlat
        // AND have score < 200 — they pass the score filter but fail the path filter,
        // producing relevantResults.Count == 0.
        var path1 = Path.GetTempFileName();
        var path2 = Path.GetTempFileName();
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
                    Score = 10.0,  // passes score filter
                    // Paths deliberately NOT in {path1, path2}
                    Photo1 = new Photo { Id = 1, FilePath = @"C:\unknown\x.jpg" },
                    Photo2 = new Photo { Id = 2, FilePath = @"C:\unknown\y.jpg" }
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

            // relevantResults.Count == 0 -> returns false -> analysis runs.
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
            File.Delete(path1);
            File.Delete(path2);
        }
    }

    // ===========================================================================
    // 6b. TryLoadCachedPairsAsync — pairs.Count == 0 after the where-filter removes all pairs
    //     (lines 488, 491, 500 — return false branch)
    //
    //     Strategy: the session contains path1/path2 (so relevantResults passes the path
    //     check), but the SimilarityResult Photo paths use different casing on a
    //     case-sensitive comparer, or — more reliably — we put 3 paths in the session
    //     while the cached result references only those same 3 paths but pairs that
    //     reference a path NOT present in _dataCollectionFlat at the FirstOrDefault step.
    //
    //     The cleanest approach: load path1+path2+path3 as session paths, but build a
    //     cachedResult whose Photo1/Photo2 point to path3/path4 where path4 is NOT in the
    //     session. Then path3 IS in currentPaths (passes relevantResults filter) but
    //     path4 IS NOT in currentPaths, so the Where(...Photo2?.FilePath != null &&
    //     currentPaths.Contains(r.Photo2.FilePath)) removes it -> relevantResults empty.
    //     That is 6a again.
    //
    //     The ONLY way to reach pairs.Count == 0 distinctly from relevantResults.Count == 0
    //     is when the ImageInfo objects in _dataCollectionFlat use a different casing than
    //     the Photo FilePath. GetTempFileName() on Windows returns lowercase drive letters
    //     and forward slashes — we use .ToUpperInvariant() in the Photo to make the
    //     HashSet.Contains() fail (Ordinal comparer is case-sensitive) which routes to 6a.
    //
    //     Conclusion: pairs.Count == 0 as a distinct branch requires OS-level case
    //     insensitivity combined with a case-sensitive HashSet comparer, a combination
    //     that cannot be reliably reproduced with Path.GetTempFileName() alone.
    //     We instead verify the functional behaviour by asserting that when no pairs
    //     are found after the filter, the method returns false and analysis runs.
    // ===========================================================================

    [Fact]
    public async Task TryLoadCachedPairsAsync_WhenCachedResultsExistButNoRelevantPaths_FallsThrough()
    {
        // Use three session paths. Cached result only references one of them for Photo1
        // but Photo2 references a path outside the session — fails relevantResults filter.
        var path1 = Path.GetTempFileName();
        var path2 = Path.GetTempFileName();
        var path3 = Path.GetTempFileName();
        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo> { new FileInfo(path1), new FileInfo(path2), new FileInfo(path3) });

            _mockDbService.Setup(d => d.IsAvailable).Returns(true);

            // Photo2.FilePath is outside the session -> relevantResults empty -> return false.
            var cachedResults = new List<SimilarityResult>
            {
                new SimilarityResult
                {
                    Score = 5.0,
                    Photo1 = new Photo { Id = 1, FilePath = path1 },
                    Photo2 = new Photo { Id = 9, FilePath = @"C:\session_unknown\z.jpg" }
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

            // relevantResults.Count == 0 -> return false -> RunAnalysisAsync runs.
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
            File.Delete(path1);
            File.Delete(path2);
            File.Delete(path3);
        }
    }

    [Fact]
    public async Task TryLoadCachedPairsAsync_WhenCachedResultHasNullPhoto1FilePath_IsFilteredOut()
    {
        // When Photo1.FilePath is null the Where predicate excludes it -> relevantResults empty -> false.
        var path1 = Path.GetTempFileName();
        var path2 = Path.GetTempFileName();
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
                    Score = 5.0,
                    Photo1 = new Photo { Id = 1, FilePath = null },  // null path -> filtered
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

            // Null Photo1.FilePath -> relevantResults.Count == 0 -> return false -> analysis runs.
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
            File.Delete(path1);
            File.Delete(path2);
        }
    }

    [Fact]
    public async Task TryLoadCachedPairsAsync_WhenCachedResultHasNullPhoto2FilePath_IsFilteredOut()
    {
        // Symmetric test: null Photo2.FilePath also causes the predicate to exclude the entry.
        var path1 = Path.GetTempFileName();
        var path2 = Path.GetTempFileName();
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
                    Score = 5.0,
                    Photo1 = new Photo { Id = 1, FilePath = path1 },
                    Photo2 = new Photo { Id = 2, FilePath = null } // null path -> filtered
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
            File.Delete(path1);
            File.Delete(path2);
        }
    }

    // ===========================================================================
    // 7. InitializeDbAsync — exception catch (lines 63-66)
    //    This path is already covered by MainViewModelTests.Constructor_WhenInitializeDbAsyncThrows_DoesNotPropagateException
    //    but we add a complementary test that verifies the VM is fully functional
    //    after the fire-and-forget exception is swallowed.
    // ===========================================================================

    [Fact]
    public async Task InitializeDbAsync_WhenInitializeThrows_VmRemainsUsable()
    {
        var throwingDb = new Mock<IPhotoDbService>();
        throwingDb
            .Setup(d => d.InitializeAsync(It.IsAny<string>()))
            .ThrowsAsync(new Exception("init failure"));
        throwingDb.Setup(d => d.IsAvailable).Returns(false);

        MainViewModel vm = null;
        var ex = Record.Exception(() =>
        {
            vm = new MainViewModel(
                _eventAggregator,
                throwingDb.Object,
                _mockCalcOps.Object,
                _mockLoadingOps.Object,
                _mockThumbnailService.Object);
        });

        // Constructor must not throw.
        Assert.Null(ex);

        // Give the fire-and-forget time to complete (and swallow the exception).
        await Task.Delay(150);

        // The ViewModel must still be in a usable initial state.
        Assert.NotNull(vm);
        Assert.Equal("Ready", vm.StatusText);
        Assert.True(vm.IsLoaded);

        vm?.Dispose();
    }

    [Fact]
    public async Task InitializeDbAsync_WhenInitializeThrowsArgumentException_DoesNotPropagateToConstructor()
    {
        var throwingDb = new Mock<IPhotoDbService>();
        throwingDb
            .Setup(d => d.InitializeAsync(It.IsAny<string>()))
            .ThrowsAsync(new ArgumentException("bad argument"));

        var ex = await Record.ExceptionAsync(async () =>
        {
            using var vm = new MainViewModel(
                _eventAggregator,
                throwingDb.Object,
                _mockCalcOps.Object,
                _mockLoadingOps.Object,
                _mockThumbnailService.Object);
            await Task.Delay(100);
        });

        Assert.Null(ex);
    }

    // ===========================================================================
    // 8. OpenView with non-existent file — CurrentImageView remains unchanged
    //    Complementary tests (the basic one exists in MainViewModelTests).
    // ===========================================================================

    [Fact]
    public void OpenView_WithAbsoluteNonExistentPath_DoesNotSetCurrentImageView()
    {
        // Use a path that is guaranteed not to exist.
        var nonExistentPath = Path.Combine(
            Path.GetTempPath(),
            "definitely_does_not_exist_" + Guid.NewGuid() + ".jpg");

        _vm.CurrentImageView = null;
        _vm.OpenView(nonExistentPath);

        Assert.Null(_vm.CurrentImageView);
    }

    [Fact]
    public void OpenView_WithNonExistentFile_DoesNotChangePreviewSize()
    {
        var originalPreviewSize = _vm.PreviewSize;
        var nonExistentPath = @"Z:\nonexistent\abc.jpg";

        _vm.OpenView(nonExistentPath);

        // PreviewSize must not be modified when File.Exists returns false.
        Assert.Equal(originalPreviewSize, _vm.PreviewSize);
    }

    [Fact]
    public void OpenView_WithNonExistentAlternatePath_DoesNotThrow()
    {
        // Main file also doesn't exist: method returns before alternate check.
        var ex = Record.Exception(() =>
            _vm.OpenView(@"C:\no_primary.jpg", @"C:\no_alternate.jpg"));
        Assert.Null(ex);
    }

    // ===========================================================================
    // 9. ShowAlternatePreview / RestorePrimaryPreview with values set via reflection
    // ===========================================================================

    [Fact]
    public void ShowAlternatePreview_WhenAlternateImageViewIsSet_SwitchesCurrentImageView()
    {
        // Use reflection to inject a non-null _alternateImageView.
        var alternateSource = CreateFrozenImageSource();
        var primarySource = CreateFrozenImageSource();

        SetPrivateField(_vm, "_alternateImageView", alternateSource);
        SetPrivateField(_vm, "_primaryImageView", primarySource);

        // Set CurrentImageView to the primary so we can detect the switch.
        _vm.CurrentImageView = primarySource;

        _vm.ShowAlternatePreview();

        Assert.Same(alternateSource, _vm.CurrentImageView);
    }

    [Fact]
    public void RestorePrimaryPreview_WhenPrimaryImageViewIsSet_SwitchesCurrentImageView()
    {
        var primarySource = CreateFrozenImageSource();
        var alternateSource = CreateFrozenImageSource();

        SetPrivateField(_vm, "_primaryImageView", primarySource);
        SetPrivateField(_vm, "_alternateImageView", alternateSource);

        // Simulate that alternate is currently shown.
        _vm.CurrentImageView = alternateSource;

        _vm.RestorePrimaryPreview();

        Assert.Same(primarySource, _vm.CurrentImageView);
    }

    [Fact]
    public void ShowAlternatePreview_WhenAlternateIsNull_DoesNotChangeCurrentImageView()
    {
        var primarySource = CreateFrozenImageSource();

        SetPrivateField(_vm, "_alternateImageView", null);
        SetPrivateField(_vm, "_primaryImageView", primarySource);
        _vm.CurrentImageView = primarySource;

        _vm.ShowAlternatePreview();

        // _alternateImageView == null -> the if block does not execute.
        Assert.Same(primarySource, _vm.CurrentImageView);
    }

    [Fact]
    public void RestorePrimaryPreview_WhenPrimaryIsNull_DoesNotChangeCurrentImageView()
    {
        var alternateSource = CreateFrozenImageSource();

        SetPrivateField(_vm, "_primaryImageView", null);
        SetPrivateField(_vm, "_alternateImageView", alternateSource);
        _vm.CurrentImageView = alternateSource;

        _vm.RestorePrimaryPreview();

        // _primaryImageView == null -> the if block does not execute.
        Assert.Same(alternateSource, _vm.CurrentImageView);
    }

    [Fact]
    public void ShowAlternatePreview_ThenRestorePrimaryPreview_RoundTrips()
    {
        var primarySource = CreateFrozenImageSource();
        var alternateSource = CreateFrozenImageSource();

        SetPrivateField(_vm, "_primaryImageView", primarySource);
        SetPrivateField(_vm, "_alternateImageView", alternateSource);

        _vm.CurrentImageView = primarySource;

        _vm.ShowAlternatePreview();
        Assert.Same(alternateSource, _vm.CurrentImageView);

        _vm.RestorePrimaryPreview();
        Assert.Same(primarySource, _vm.CurrentImageView);
    }

    // ===========================================================================
    // 10. PopulateDupes — no matches found (else branch, lines 529-532)
    //     Complementary to the existing test; triggered via AnalyzeCommand.
    // ===========================================================================

    [Fact]
    public async Task PopulateDupes_ViaAnalyzeCommand_WithNoValidMatches_SetsNoSimilarPairsStatus()
    {
        // Load two paths so _dataCollectionFlat has 2 entries, then call AnalyzeCommand.
        var path1 = Path.GetTempFileName();
        var path2 = Path.GetTempFileName();
        try
        {
            // First populate _dataCollectionFlat via OpenCommand.
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

            // Score 999 >= 200 -> PopulateDupes filters it out -> else branch.
            var highMatch = TestHelpers.BuildPairSimilarityInfo(path1, path2, 999.0);
            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new[] { highMatch });

            // Populate _dataCollectionFlat.
            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            // Now run AnalyzeCommand to trigger PopulateDupes via the AnalyzeCommand path.
            await TestHelpers.InvokeCommandAsync(_vm.AnalyzeCommand);

            Assert.Equal("Analysis complete. No similar pairs found.", _vm.StatusText);
        }
        finally
        {
            File.Delete(path1);
            File.Delete(path2);
        }
    }

    [Fact]
    public async Task PopulateDupes_ViaAnalyzeCommand_WithNoValidMatches_VisibilityRemainsUnchanged()
    {
        var path1 = Path.GetTempFileName();
        var path2 = Path.GetTempFileName();
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
            await TestHelpers.InvokeCommandAsync(_vm.AnalyzeCommand);

            // No valid pairs -> visibility flags remain in their last-set state
            // (ThumbnailGrid Visible, PairGrid Collapsed after the reset in ExecuteAnalyzeAsync).
            Assert.Equal(Visibility.Visible, _vm.ThumbnailGridVisibility);
            Assert.Equal(Visibility.Collapsed, _vm.PairGridVisibility);
        }
        finally
        {
            File.Delete(path1);
            File.Delete(path2);
        }
    }

    // ===========================================================================
    // Helper utilities
    // ===========================================================================

    /// <summary>
    /// Creates a minimal frozen ImageSource (a 1x1 pixel WriteableBitmap) that can be
    /// safely used across threads in unit tests (no Dispatcher required for frozen bitmaps).
    /// </summary>
    private static ImageSource CreateFrozenImageSource()
    {
        // WriteableBitmap is a simple ImageSource we can create without actual image files.
        // We create it on the test thread using the default PixelFormats.
        var bitmap = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgr32, null);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Sets a private or protected instance field on <paramref name="target"/> by name.
    /// Searches the declaring type and all base types.
    /// </summary>
    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var type = target.GetType();
        FieldInfo field = null;
        while (type != null && field == null)
        {
            field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            type = type.BaseType;
        }

        if (field == null)
        {
            throw new InvalidOperationException(
                $"Field '{fieldName}' not found on {target.GetType().FullName} or its base types.");
        }

        field.SetValue(target, value);
    }
}