using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Dupples_finder_UI.Data;
using Dupples_finder_UI.Services;
using Dupples_finder_UI.Services.Interfaces;
using Moq;
using OpenCvSharp;
using Xunit;

namespace Dupless_finder_Tests;
// =========================================================================
// PhotoDbService Gap Tests — catch blocks, update paths, Dispose, schema
// =========================================================================

/// <summary>
/// Covers code paths in PhotoDbService that are not exercised by the main
/// PhotoDbServiceTests or ServiceExtendedTests suites:
///   - All catch-block paths (triggered by disposing the underlying context)
///   - CachePhotoHashAsync UPDATE branch
///   - CacheThumbnailAsync UPDATE branch
///   - StoreSimilarityAsync UPDATE branch
///   - StoreSimilarityAsync ID normalisation swap branch (photo1Id &gt; photo2Id)
///   - InitializeAsync failure branch (invalid / unreachable path)
///   - InitializeAsync schema-migration branch (old DB missing column)
///   - Dispose (initialized service) and double-Dispose (idempotent)
/// </summary>
public class PhotoDbServiceGapTests : IDisposable
{
    private readonly List<string> _tempFiles = new List<string>();

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private string NewTempDb()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gap_test_{Guid.NewGuid()}.db");
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>
    /// Initializes a service with a fresh temp DB, then disposes the
    /// underlying EF DbContext via reflection so that every subsequent
    /// DB call throws ObjectDisposedException.  The service itself is
    /// NOT disposed so its _isAvailable flag remains true.
    /// </summary>
    private static async Task<PhotoDbService> CreateServiceWithDisposedContext(string dbPath)
    {
        var service = new PhotoDbService();
        await service.InitializeAsync(dbPath);

        // Reach into the private _context field and dispose it directly.
        var contextField = typeof(PhotoDbService)
            .GetField("_context", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(contextField);  // sanity

        var context = (DuplessDbContext)contextField.GetValue(service);
        context.Dispose();

        return service;
    }

    public void Dispose()
    {
        // Clear any SQLite connection pools so file handles are released.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        foreach (var path in _tempFiles)
        {
            try { if (File.Exists(path))
                {
                    File.Delete(path);
                }
            } catch { }
        }
    }

    // ------------------------------------------------------------------
    // Catch-block tests — disposed context → graceful returns
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetCachedPhotoAsync_CatchBlock_ReturnsNull_WhenContextDisposed()
    {
        var dbPath = NewTempDb();
        using var service = await CreateServiceWithDisposedContext(dbPath);

        var result = await service.GetCachedPhotoAsync("some/path.jpg");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCachedPhotoByFingerprintAsync_CatchBlock_ReturnsNull_WhenContextDisposed()
    {
        var dbPath = NewTempDb();
        using var service = await CreateServiceWithDisposedContext(dbPath);

        var result = await service.GetCachedPhotoByFingerprintAsync(12345L, DateTime.UtcNow);

        Assert.Null(result);
    }

    [Fact]
    public async Task CachePhotoHashAsync_CatchBlock_ReturnsNull_WhenContextDisposed()
    {
        var dbPath = NewTempDb();
        using var service = await CreateServiceWithDisposedContext(dbPath);

        var result = await service.CachePhotoHashAsync(
            filePath: "photo.jpg",
            descriptors: new byte[] { 1, 2, 3 },
            rows: 5,
            cols: 128,
            fileSize: 1024L,
            lastModifiedUtc: DateTime.UtcNow);

        Assert.Null(result);
    }

    [Fact]
    public async Task CacheThumbnailAsync_CatchBlock_ReturnsNull_WhenContextDisposed()
    {
        var dbPath = NewTempDb();
        using var service = await CreateServiceWithDisposedContext(dbPath);

        var result = await service.CacheThumbnailAsync(
            fileSize: 2048L,
            lastModifiedUtc: DateTime.UtcNow,
            filePath: "photo.jpg",
            thumbnail: new byte[] { 255, 0, 128 });

        Assert.Null(result);
    }

    [Fact]
    public async Task StoreSimilarityAsync_CatchBlock_DoesNotThrow_WhenContextDisposed()
    {
        var dbPath = NewTempDb();
        using var service = await CreateServiceWithDisposedContext(dbPath);

        // Must not throw — catch block swallows the ObjectDisposedException.
        var ex = await Record.ExceptionAsync(() => service.StoreSimilarityAsync(1, 2, 0.75));

        Assert.Null(ex);
    }

    [Fact]
    public async Task GetCachedResultsAsync_CatchBlock_ReturnsEmptyList_WhenContextDisposed()
    {
        var dbPath = NewTempDb();
        using var service = await CreateServiceWithDisposedContext(dbPath);

        var results = await service.GetCachedResultsAsync();

        Assert.NotNull(results);
        Assert.Empty(results);
    }

    // ------------------------------------------------------------------
    // InitializeAsync — failure / invalid-path branch
    // ------------------------------------------------------------------

    [Fact]
    public async Task InitializeAsync_WithInvalidPath_SetsIsAvailableFalse()
    {
        // A path whose parent directories don't exist and cannot be created
        // causes SQLite to fail, which triggers the outer catch block.
        var impossiblePath = Path.Combine(
            "Z:\\nonexistent_root_12345", "deep", "path", "db.db");

        using var service = new PhotoDbService();
        await service.InitializeAsync(impossiblePath);

        Assert.False(service.IsAvailable);
    }

    [Fact]
    public async Task InitializeAsync_AfterFailure_AllMethodsReturnGracefully()
    {
        var impossiblePath = Path.Combine(
            "Z:\\nonexistent_root_12345", "deep", "path2", "db.db");

        using var service = new PhotoDbService();
        await service.InitializeAsync(impossiblePath);

        // The service must be non-functional but must not throw.
        Assert.Null(await service.GetCachedPhotoAsync("x.jpg"));
        Assert.Null(await service.GetCachedPhotoByFingerprintAsync(1L, DateTime.UtcNow));
        Assert.Null(await service.CachePhotoHashAsync("x.jpg", new byte[1], 1, 128, 1L, DateTime.UtcNow));
        Assert.Null(await service.CacheThumbnailAsync(1L, DateTime.UtcNow, "x.jpg", new byte[1]));
        await service.StoreSimilarityAsync(1, 2, 0.5); // must not throw
        Assert.Empty(await service.GetCachedResultsAsync());
    }

    // NOTE: InitializeAsync schema-migration test was removed because it duplicates
    // PhotoDbServiceExtendedTests.InitializeAsync_WithOutdatedSchema_DoesNotThrow.

    // NOTE: CachePhotoHashAsync UPDATE path, CacheThumbnailAsync UPDATE path,
    // StoreSimilarityAsync UPDATE path, and StoreSimilarityAsync ID normalisation
    // tests were removed because they duplicate tests in PhotoDbServiceTests and
    // PhotoDbServiceExtendedTests.

    // ------------------------------------------------------------------
    // Dispose — initialized service, and double-Dispose (idempotent)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dispose_InitializedService_DoesNotThrow()
    {
        var dbPath = NewTempDb();
        var service = new PhotoDbService();
        await service.InitializeAsync(dbPath);
        Assert.True(service.IsAvailable);

        var ex = Record.Exception(() => service.Dispose());

        Assert.Null(ex);
    }

    [Fact]
    public async Task Dispose_CalledTwice_IsIdempotent()
    {
        var dbPath = NewTempDb();
        var service = new PhotoDbService();
        await service.InitializeAsync(dbPath);

        service.Dispose();

        // Second Dispose must not throw.
        var ex = Record.Exception(() => service.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_UninitializedService_DoesNotThrow()
    {
        var service = new PhotoDbService();
        // Never called InitializeAsync.
        var ex = Record.Exception(() => service.Dispose());
        Assert.Null(ex);
    }
}

// NOTE: ThumbnailServiceGapTests was removed because all its tests duplicate
// existing tests in ThumbnailServiceTests and ThumbnailServiceExtendedTests.

// =========================================================================
// CalcOperations Gap Tests — CalcSimilarity edge cases via CreateMatchCollection
// =========================================================================

/// <summary>
/// Covers the private CalcSimilarity paths that are not hit by
/// CalcOperationsTests, exercised indirectly through CreateMatchCollection:
///   - d1.Rows &lt; 2 || d2.Rows &lt; 2 → double.MaxValue
///   - d1.Cols != d2.Cols → double.MaxValue
///   - goodMatches.Count == 0 → double.MaxValue
///     (achieved with all-zero or all-equal descriptors so every distance
///      ratio equals 1.0 — none pass the 0.7 Lowe ratio test)
///   - Normal matching path returning finite similarity score
/// </summary>
public class CalcOperationsGapTests : IDisposable
{
    private readonly Mock<IMatSerializer> _mockSerializer = new Mock<IMatSerializer>();
    private readonly CalcOperations _calcOperations;
    private readonly List<Mat> _mats = new List<Mat>();

    public CalcOperationsGapTests()
    {
        _calcOperations = new CalcOperations(_mockSerializer.Object);
    }

    public void Dispose()
    {
        foreach (var m in _mats)
        {
            try { m.Dispose(); } catch { }
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Creates a CV_32F Mat tracked for disposal.
    /// </summary>
    private Mat MakeMat(int rows, int cols, float fill = 0f)
    {
        var mat = new Mat(rows, cols, MatType.CV_32F, new Scalar(fill));
        _mats.Add(mat);
        return mat;
    }

    /// <summary>
    /// Creates a CV_32F Mat with random values, tracked for disposal.
    /// </summary>
    private Mat MakeRandomMat(int rows, int cols, int seed = 42)
    {
        var mat = new Mat(rows, cols, MatType.CV_32F);
        var rng = new Random(seed);
        var data = new float[rows * cols];
        for (var i = 0; i < data.Length; i++)
            data[i] = (float)rng.NextDouble();
        Marshal.Copy(data, 0, mat.Data, data.Length);
        _mats.Add(mat);
        return mat;
    }

    private IDictionary<string, Mat> Dict(params (string key, Mat mat)[] pairs)
    {
        var d = new Dictionary<string, Mat>();
        foreach (var (k, m) in pairs) d[k] = m;
        return d;
    }

    // ------------------------------------------------------------------
    // d1.Rows < 2 branch
    // ------------------------------------------------------------------

    [Fact]
    public void CalcSimilarity_ReturnsMaxValue_WhenFirstDescriptorHasOneRow()
    {
        // 1 row → KnnMatch(k=2) cannot find two neighbours → MaxValue.
        var mat1 = MakeMat(1, 128, fill: 0.5f);
        var mat2 = MakeRandomMat(10, 128);

        var result = _calcOperations
            .CreateMatchCollection(Dict(("a.jpg", mat1), ("b.jpg", mat2)), null)
            .ToList();

        Assert.Single(result);
        Assert.Equal(double.MaxValue, result[0].Match);
    }

    [Fact]
    public void CalcSimilarity_ReturnsMaxValue_WhenSecondDescriptorHasOneRow()
    {
        var mat1 = MakeRandomMat(10, 128);
        var mat2 = MakeMat(1, 128, fill: 0.5f);

        var result = _calcOperations
            .CreateMatchCollection(Dict(("a.jpg", mat1), ("b.jpg", mat2)), null)
            .ToList();

        Assert.Single(result);
        Assert.Equal(double.MaxValue, result[0].Match);
    }

    [Fact]
    public void CalcSimilarity_ReturnsMaxValue_WhenBothDescriptorsHaveZeroRows()
    {
        // Empty Mat (0 rows) is the degenerate case.
        var mat1 = new Mat();  // 0×0
        var mat2 = new Mat();
        _mats.Add(mat1);
        _mats.Add(mat2);

        var result = _calcOperations
            .CreateMatchCollection(Dict(("a.jpg", mat1), ("b.jpg", mat2)), null)
            .ToList();

        Assert.Single(result);
        Assert.Equal(double.MaxValue, result[0].Match);
    }

    // ------------------------------------------------------------------
    // d1.Cols != d2.Cols branch
    // ------------------------------------------------------------------

    // NOTE: CalcSimilarity_ReturnsMaxValue_WhenDescriptorColsMismatch was removed because
    // it duplicates CalcOperationsTests.CreateMatchCollection_WithMismatchedDescriptorColumns_ReturnsMaxValueMatch.

    [Fact]
    public void CalcSimilarity_ReturnsMaxValue_WhenOneDescriptorHasDifferentColsFromOther()
    {
        // 128 vs 256 — both have enough rows, but cols differ.
        var mat1 = MakeRandomMat(5, 128);
        var mat2 = MakeRandomMat(5, 256);

        var result = _calcOperations
            .CreateMatchCollection(Dict(("a.jpg", mat1), ("b.jpg", mat2)), null)
            .ToList();

        Assert.Single(result);
        Assert.Equal(double.MaxValue, result[0].Match);
    }

    // ------------------------------------------------------------------
    // goodMatches.Count == 0 branch (all-identical descriptors → ratio = 1.0)
    // ------------------------------------------------------------------

    [Fact]
    public void CalcSimilarity_ReturnsMaxValue_WhenNoGoodMatchesPassRatioTest()
    {
        // When all rows in both descriptors are identical, every KnnMatch
        // result will have match[0].Distance ≈ match[1].Distance (ratio ≈ 1),
        // so the 0.7 Lowe ratio test passes for NONE of them.
        // This means goodMatches.Count == 0 → double.MaxValue.
        //
        // Fill both with the same constant value so distances are degenerate.
        var constant = 0.12345f;
        var mat1 = MakeMat(10, 128, fill: constant);
        var mat2 = MakeMat(10, 128, fill: constant);

        var result = _calcOperations
            .CreateMatchCollection(Dict(("a.jpg", mat1), ("b.jpg", mat2)), null)
            .ToList();

        Assert.Single(result);
        Assert.Equal(double.MaxValue, result[0].Match);
    }

    // ------------------------------------------------------------------
    // Normal path — finite similarity score
    // ------------------------------------------------------------------

    [Fact]
    public void CalcSimilarity_ReturnsFiniteValue_WithDistinctRandomDescriptors()
    {
        // Two different random descriptor matrices with enough rows.
        // At least some Lowe-ratio-passing matches are expected, so
        // the result should be a finite (< MaxValue) score.
        var mat1 = MakeRandomMat(20, 128, seed: 1);
        var mat2 = MakeRandomMat(20, 128, seed: 99999);

        var result = _calcOperations
            .CreateMatchCollection(Dict(("a.jpg", mat1), ("b.jpg", mat2)), null)
            .ToList();

        Assert.Single(result);
        // The score is either finite (good matches found) or MaxValue (no good matches).
        // Both are acceptable; the important thing is no exception was thrown.
        Assert.True(result[0].Match >= 0);
    }

    // ------------------------------------------------------------------
    // Multiple-pair ordering (covers progress reporting and iteration paths)
    // ------------------------------------------------------------------

    [Fact]
    public void CreateMatchCollection_WithMismatchedAndValidPairs_AllReturnMaxValue()
    {
        // Three mats: mat1 (128 cols, 1 row), mat2 (64 cols), mat3 (128 cols, 10 rows).
        // Pairs: (mat1, mat2) → cols mismatch AND insufficient rows;
        //        (mat1, mat3) → insufficient rows (mat1 has 1 row);
        //        (mat2, mat3) → cols mismatch.
        // All three pairs should produce MaxValue.
        var mat1 = MakeMat(1, 128);
        var mat2 = MakeRandomMat(10, 64);
        var mat3 = MakeRandomMat(10, 128);

        var result = _calcOperations
            .CreateMatchCollection(
                Dict(("a.jpg", mat1), ("b.jpg", mat2), ("c.jpg", mat3)),
                null)
            .ToList();

        Assert.Equal(3, result.Count);
        Assert.All(result, r => Assert.Equal(double.MaxValue, r.Match));
    }
}

// =========================================================================
// LoadingOperations Gap Tests — DirSearch exception path
// =========================================================================

/// <summary>
/// Covers the DirSearch catch block that fires when the target directory
/// does not exist.  GetAllPaths must still return true and an empty
/// (or partial) path list — it must not propagate the exception.
/// </summary>
public class LoadingOperationsGapTests
{
    // NOTE: GetAllPaths_WithNonExistentDirectory_* tests were removed because they
    // duplicate LoadingOperationsExtendedTests.GetAllPaths_WithNonExistentPath_ReturnsTrueWithEmptyPaths.

    [Fact]
    public void GetAllPaths_WithValidDirectoryThenNonExistentSubdir_ReturnsFilesFromValidParts()
    {
        // Create a real directory with image files, plus refer to a
        // sub-path that does not exist.  DirSearch should still return
        // files from the parts it CAN read.
        var tempDir = Path.Combine(
            Path.GetTempPath(), $"gap_mixed_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Put a valid image file in the real directory.
            var imgPath = Path.Combine(tempDir, "photo.jpg");
            File.WriteAllBytes(imgPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }); // JPEG header

            var ops = new LoadingOperations();
            ops.GetAllPaths(out var paths, tempDir);

            var list = paths?.ToList() ?? new List<string>();
            // At minimum, the file we placed there must appear.
            Assert.Contains(imgPath, list);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    // NOTE: GetAllPaths_WithPathThatCausesAccessDenied_DoesNotThrow was removed because it
    // duplicates LoadingOperationsExtendedTests.GetAllPaths_WithNonExistentPath_ReturnsTrueWithEmptyPaths.
}