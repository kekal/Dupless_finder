using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
/// Tests for the optimized StoreSimilarityResultsAsync method in MainViewModel.
/// This method is private, so it's tested through the RunAnalysisAsync -> AnalyzeCommand flow.
///
/// The optimization:
/// - Collects unique file paths from all matches (instead of calling GetCachedPhotoAsync twice per match).
/// - Does ONE GetCachedPhotoAsync call per unique path.
/// - Uses pre-resolved IDs to store similarity scores.
///
/// Tests verify:
/// 1. Unique path lookup: When multiple matches share the same paths, GetCachedPhotoAsync
///    should be called exactly once per unique path.
/// 2. StoreSimilarityAsync called correctly: Correct pre-resolved IDs are passed.
/// 3. double.MaxValue matches are filtered out.
/// </summary>
public class StoreSimilarityOptTests : IDisposable
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

    public StoreSimilarityOptTests()
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
        if (SynchronizationContext.Current == null)
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronousSyncContext());
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
    // 1. Unique path lookup: 3 matches sharing 3 paths -> 3 GetCachedPhotoAsync calls
    // ===========================================================================

    [Fact]
    public async Task StoreSimilarityOpt_UniquePaths_CallsGetCachedPhotoOncePerPath()
    {
        // Arrange: 3 matches that share the same 3 unique paths.
        // Match 1: path1 <-> path2 (score 50)
        // Match 2: path1 <-> path3 (score 60)
        // Match 3: path2 <-> path3 (score 70)
        // Total unique paths: {path1, path2, path3} = 3
        // Old behavior would call GetCachedPhotoAsync 6 times (2 per match).
        // Optimized behavior calls it 3 times (once per unique path).

        var path1 = Path.GetTempFileName();
        var path2 = Path.GetTempFileName();
        var path3 = Path.GetTempFileName();

        try
        {
            // Set up folder dialog and file scanning
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo>
                {
                    new FileInfo(path1),
                    new FileInfo(path2),
                    new FileInfo(path3)
                });

            // DB is available so StoreSimilarityResultsAsync body is entered.
            _mockDbService.Setup(d => d.IsAvailable).Returns(true);

            // GetCachedResultsAsync returns empty so TryLoadCachedPairsAsync returns false.
            _mockDbService
                .Setup(d => d.GetCachedResultsAsync())
                .ReturnsAsync(new List<SimilarityResult>());

            // Set up CalcSiftHashes
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

            // Set up CreateMatchCollection to return the 3 matches with the same 3 paths
            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new List<PairSimilarityInfo>
                {
                    TestHelpers.BuildPairSimilarityInfo(path1, path2, 50.0),  // Match 1
                    TestHelpers.BuildPairSimilarityInfo(path1, path3, 60.0),  // Match 2
                    TestHelpers.BuildPairSimilarityInfo(path2, path3, 70.0)   // Match 3
                });

            // Set up GetCachedPhotoAsync to return photos with IDs
            _mockDbService
                .Setup(d => d.GetCachedPhotoAsync(path1))
                .ReturnsAsync(new Photo { Id = 1, FilePath = path1 });
            _mockDbService
                .Setup(d => d.GetCachedPhotoAsync(path2))
                .ReturnsAsync(new Photo { Id = 2, FilePath = path2 });
            _mockDbService
                .Setup(d => d.GetCachedPhotoAsync(path3))
                .ReturnsAsync(new Photo { Id = 3, FilePath = path3 });

            // Invoke the OpenCommand which loads images and calls RunAnalysisAsync -> StoreSimilarityResultsAsync
            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            // Assert: GetCachedPhotoAsync should be called exactly 3 times (once per unique path),
            // not 6 times (2 per match pair).
            _mockDbService.Verify(
                d => d.GetCachedPhotoAsync(It.IsAny<string>()),
                Times.Exactly(3),
                "GetCachedPhotoAsync should be called exactly 3 times (once per unique path)");
        }
        finally
        {
            File.Delete(path1);
            File.Delete(path2);
            File.Delete(path3);
        }
    }

    // ===========================================================================
    // 2. StoreSimilarityAsync called with correct pre-resolved IDs
    // ===========================================================================

    [Fact]
    public async Task StoreSimilarityOpt_CallsStoreSimilarityWithCorrectIds()
    {
        // Arrange: Create 2 matches with known paths and IDs.
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

            // Set up CalcSiftHashes
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

            // Set up CreateMatchCollection
            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new List<PairSimilarityInfo>
                {
                    TestHelpers.BuildPairSimilarityInfo(path1, path2, 45.5)
                });

            // Set up GetCachedPhotoAsync with specific IDs
            _mockDbService
                .Setup(d => d.GetCachedPhotoAsync(path1))
                .ReturnsAsync(new Photo { Id = 10, FilePath = path1 });
            _mockDbService
                .Setup(d => d.GetCachedPhotoAsync(path2))
                .ReturnsAsync(new Photo { Id = 20, FilePath = path2 });

            // Invoke the OpenCommand which loads images and calls RunAnalysisAsync -> StoreSimilarityResultsAsync
            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            // Assert: StoreSimilarityAsync should be called with IDs 10, 20, and score 45.5
            _mockDbService.Verify(
                d => d.StoreSimilarityAsync(10, 20, 45.5),
                Times.Once,
                "StoreSimilarityAsync should be called with correct pre-resolved IDs");
        }
        finally
        {
            File.Delete(path1);
            File.Delete(path2);
        }
    }

    // ===========================================================================
    // 3. double.MaxValue matches are filtered out
    // ===========================================================================

    [Fact]
    public async Task StoreSimilarityOpt_SkipsDoubleMaxValueMatches()
    {
        // Arrange: Create 3 matches, where 2 are valid and 1 is double.MaxValue (should be skipped).
        var path1 = Path.GetTempFileName();
        var path2 = Path.GetTempFileName();
        var path3 = Path.GetTempFileName();

        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo>
                {
                    new FileInfo(path1),
                    new FileInfo(path2),
                    new FileInfo(path3)
                });

            _mockDbService.Setup(d => d.IsAvailable).Returns(true);

            _mockDbService
                .Setup(d => d.GetCachedResultsAsync())
                .ReturnsAsync(new List<SimilarityResult>());

            // Set up CalcSiftHashes
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

            // Set up CreateMatchCollection with 2 valid matches and 1 MaxValue match
            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new List<PairSimilarityInfo>
                {
                    TestHelpers.BuildPairSimilarityInfo(path1, path2, 40.0),      // Valid
                    TestHelpers.BuildPairSimilarityInfo(path1, path3, double.MaxValue), // Should be skipped
                    TestHelpers.BuildPairSimilarityInfo(path2, path3, 50.0)       // Valid
                });

            // Set up GetCachedPhotoAsync
            _mockDbService
                .Setup(d => d.GetCachedPhotoAsync(path1))
                .ReturnsAsync(new Photo { Id = 1, FilePath = path1 });
            _mockDbService
                .Setup(d => d.GetCachedPhotoAsync(path2))
                .ReturnsAsync(new Photo { Id = 2, FilePath = path2 });
            _mockDbService
                .Setup(d => d.GetCachedPhotoAsync(path3))
                .ReturnsAsync(new Photo { Id = 3, FilePath = path3 });

            // Invoke the OpenCommand which loads images and calls RunAnalysisAsync -> StoreSimilarityResultsAsync
            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            // Assert: GetCachedPhotoAsync should only be called for path1, path2, path3 once each.
            // But since path3 is only in the MaxValue match, it should still be fetched.
            // Actually, all 3 paths will be collected in uniquePaths because they're all referenced.
            // However, StoreSimilarityAsync should only be called twice (for the valid matches).
            _mockDbService.Verify(
                d => d.StoreSimilarityAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<double>()),
                Times.Exactly(2),
                "StoreSimilarityAsync should be called exactly 2 times (skipping the MaxValue match)");

            // Verify the two valid calls
            _mockDbService.Verify(
                d => d.StoreSimilarityAsync(1, 2, 40.0),
                Times.Once);
            _mockDbService.Verify(
                d => d.StoreSimilarityAsync(2, 3, 50.0),
                Times.Once);
        }
        finally
        {
            File.Delete(path1);
            File.Delete(path2);
            File.Delete(path3);
        }
    }

    // ===========================================================================
    // 4. Only MaxValue matches: GetCachedPhotoAsync not called, StoreSimilarity not called
    // ===========================================================================

    [Fact]
    public async Task StoreSimilarityOpt_OnlyMaxValueMatches_SkipsEverything()
    {
        // Arrange: All matches are double.MaxValue, so nothing should be stored.
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

            // Set up CalcSiftHashes
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

            // Set up CreateMatchCollection with only MaxValue match
            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new List<PairSimilarityInfo>
                {
                    TestHelpers.BuildPairSimilarityInfo(path1, path2, double.MaxValue)
                });

            // Invoke the OpenCommand which loads images and calls RunAnalysisAsync -> StoreSimilarityResultsAsync
            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            // Assert: GetCachedPhotoAsync should not be called
            _mockDbService.Verify(
                d => d.GetCachedPhotoAsync(It.IsAny<string>()),
                Times.Never,
                "GetCachedPhotoAsync should not be called when all matches are MaxValue");

            // Assert: StoreSimilarityAsync should not be called
            _mockDbService.Verify(
                d => d.StoreSimilarityAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<double>()),
                Times.Never,
                "StoreSimilarityAsync should not be called when all matches are MaxValue");
        }
        finally
        {
            File.Delete(path1);
            File.Delete(path2);
        }
    }

    // ===========================================================================
    // 5. Partial path resolution: Some paths not found in DB
    // ===========================================================================

    [Fact]
    public async Task StoreSimilarityOpt_PartialPathResolution_OnlyStoresFoundPairs()
    {
        // Arrange: 3 paths, but path3 is not found in DB (returns null).
        // Match 1: path1 <-> path2 (should be stored with IDs)
        // Match 2: path1 <-> path3 (should NOT be stored because path3 not found)
        var path1 = Path.GetTempFileName();
        var path2 = Path.GetTempFileName();
        var path3 = Path.GetTempFileName();

        try
        {
            _mockLoadingOps.Setup(l => l.ShowFolderDialog()).Returns(@"C:\test");
            _mockLoadingOps
                .Setup(l => l.ScanImageFileInfos(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>>(), It.IsAny<CancellationToken>()))
                .Returns(new List<FileInfo>
                {
                    new FileInfo(path1),
                    new FileInfo(path2),
                    new FileInfo(path3)
                });

            _mockDbService.Setup(d => d.IsAvailable).Returns(true);

            _mockDbService
                .Setup(d => d.GetCachedResultsAsync())
                .ReturnsAsync(new List<SimilarityResult>());

            // Set up CalcSiftHashes
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

            // Set up CreateMatchCollection
            _mockCalcOps
                .Setup(c => c.CreateMatchCollection(
                    It.IsAny<IDictionary<string, Mat>>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new List<PairSimilarityInfo>
                {
                    TestHelpers.BuildPairSimilarityInfo(path1, path2, 55.0),
                    TestHelpers.BuildPairSimilarityInfo(path1, path3, 65.0)
                });

            // Set up GetCachedPhotoAsync: path1 and path2 found, path3 not found
            _mockDbService
                .Setup(d => d.GetCachedPhotoAsync(path1))
                .ReturnsAsync(new Photo { Id = 100, FilePath = path1 });
            _mockDbService
                .Setup(d => d.GetCachedPhotoAsync(path2))
                .ReturnsAsync(new Photo { Id = 200, FilePath = path2 });
            _mockDbService
                .Setup(d => d.GetCachedPhotoAsync(path3))
                .ReturnsAsync((Photo)null);  // Not found

            // Invoke the OpenCommand which loads images and calls RunAnalysisAsync -> StoreSimilarityResultsAsync
            await TestHelpers.InvokeCommandAsync(_vm.OpenCommand);

            // Assert: GetCachedPhotoAsync should be called 3 times (once per unique path)
            _mockDbService.Verify(
                d => d.GetCachedPhotoAsync(It.IsAny<string>()),
                Times.Exactly(3));

            // Assert: StoreSimilarityAsync should be called only once (for the pair that both paths were found)
            _mockDbService.Verify(
                d => d.StoreSimilarityAsync(100, 200, 55.0),
                Times.Once);

            // The second pair should not be stored because path3 was not found
            _mockDbService.Verify(
                d => d.StoreSimilarityAsync(100, It.IsAny<int>(), 65.0),
                Times.Never);
        }
        finally
        {
            File.Delete(path1);
            File.Delete(path2);
            File.Delete(path3);
        }
    }
}
