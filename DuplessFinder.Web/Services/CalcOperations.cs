using DuplessFinder.Web.Models;

namespace DuplessFinder.Web.Services;

public class CalcOperations : ICalcOperations
{
    private readonly IOpenCvService _opencvService;
    private readonly ICacheService _cacheService;
    private readonly IFileAccessService _fileAccess;
    private readonly ILogger<CalcOperations> _logger;

    public CalcOperations(
        IOpenCvService opencvService,
        ICacheService cacheService,
        IFileAccessService fileAccess,
        ILogger<CalcOperations> logger)
    {
        _opencvService = opencvService;
        _cacheService = cacheService;
        _fileAccess = fileAccess;
        _logger = logger;
    }

    /// <summary>
    /// Computes SIFT descriptors for all images, using fingerprint-based IndexedDB cache.
    /// Cache lookups use the ImageInfo's Fingerprint (FileSize + LastModified).
    /// For cache misses: reads file bytes, computes SIFT via OpenCV.js Web Workers,
    /// then caches the result in IndexedDB.
    /// WASM is single-threaded, so images are processed sequentially from C#,
    /// but the actual SIFT computation is dispatched to JS Web Workers.
    /// </summary>
    public async Task<(Dictionary<string, SiftResult> HashDict, Dictionary<string, string> PathToFingerprint)> CalcSiftHashesAsync(
        IList<ImageInfo> images,
        IProgress<double> progress,
        CancellationToken ct = default)
    {
        _logger.LogInformation("CalcSiftHashesAsync started");

        var hashDict = new Dictionary<string, SiftResult>();
        var pathToFingerprint = new Dictionary<string, string>();
        var total = images.Count;

        progress?.Report(0);

        for (var i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();

            // Yield every 10 iterations to prevent UI freezing
            if (i > 0 && i % 10 == 0)
                await Task.Yield();

            var image = images[i];
            pathToFingerprint[image.Path] = image.Fingerprint;

            try
            {
                var cached = await _cacheService.GetSiftDescriptorAsync(image.Fingerprint);
                if (cached is { Data.Length: > 0 })
                {
                    hashDict[image.Path] = new SiftResult(cached.Rows, cached.Cols, cached.Data);
                    ReportProgress(progress, i + 1, total);
                    continue;
                }

                var fileBytes = await _fileAccess.ReadFileBytesAsync(image.Path);
                if (fileBytes is not { Length: > 0 })
                {
                    _logger.LogWarning("Skipping '{ImageName}': empty file bytes.", image.Name);
                    ReportProgress(progress, i + 1, total);
                    continue;
                }

                var siftResult = await _opencvService.ComputeSiftAsync(fileBytes, image.Fingerprint, image.Name);
                if (siftResult?.DescriptorData is not { Length: > 0 })
                {
                    _logger.LogWarning("Skipping '{ImageName}': no SIFT keypoints detected.", image.Name);
                    ReportProgress(progress, i + 1, total);
                    continue;
                }

                hashDict[image.Path] = siftResult;

                try
                {
                    await _cacheService.PutSiftDescriptorAsync(
                        image.Fingerprint,
                        siftResult.DescriptorData,
                        siftResult.Rows,
                        siftResult.Cols);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Cache store failed for '{ImageName}': {Message}", image.Name, ex.Message);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SIFT computation failed for '{ImageName}': {Message}", image.Name, ex.Message);
            }

            ReportProgress(progress, i + 1, total);
        }

        _logger.LogInformation("CalcSiftHashesAsync completed: {Processed}/{Total} images processed.", hashDict.Count, total);
        progress?.Report(100);
        return (hashDict, pathToFingerprint);
    }

    /// <summary>
    /// Creates all unique image pairs and computes similarity scores.
    /// Total pairs = n*(n-1)/2. Each pair is matched via OpenCV.js FLANN matcher
    /// dispatched to Web Workers.
    /// Score = 1000.0 / goodMatchCount, or double.MaxValue if no good matches.
    /// Returns results ordered by score (most similar first).
    /// </summary>
    public async Task<List<PairSimilarityInfo>> CreateMatchCollectionAsync(
        Dictionary<string, SiftResult> hashDict,
        Dictionary<string, string> pathToFingerprint,
        IProgress<double>? progress,
        CancellationToken ct = default)
    {
        _logger.LogInformation("CreateMatchCollectionAsync started");

        var similarities = new List<PairSimilarityInfo>();
        var hashes = hashDict.ToArray();
        var pairCount = hashes.Length * (hashes.Length - 1) / 2;
        var completed = 0;

        progress?.Report(0);

        for (var j = 0; j < hashes.Length; j++)
        {
            for (var i = j + 1; i < hashes.Length; i++)
            {
                ct.ThrowIfCancellationRequested();

                // Yield every 10 iterations to prevent UI freezing
                if (completed > 0 && completed % 10 == 0)
                    await Task.Yield();

                var entry1 = hashes[j];
                var entry2 = hashes[i];

                try
                {
                    var score = await _opencvService.MatchPairAsync(entry1.Value, entry2.Value);

                    pathToFingerprint.TryGetValue(entry1.Key, out var fp1);
                    pathToFingerprint.TryGetValue(entry2.Key, out var fp2);

                    similarities.Add(new PairSimilarityInfo(
                        entry1.Key, entry2.Key,
                        fp1 ?? entry1.Key, fp2 ?? entry2.Key,
                        score));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Match failed for pair ({Path1}, {Path2}): {Message}", entry1.Key, entry2.Key, ex.Message);
                }

                completed++;
                ReportProgress(progress, completed, pairCount);
            }
        }

        _logger.LogInformation("CreateMatchCollectionAsync completed: {Count} pairs scored.", similarities.Count);
        progress?.Report(100);

        return similarities.OrderBy(s => s.Score).ToList();
    }

    private static void ReportProgress(IProgress<double>? progress, int completed, int total)
    {
        if (progress is null || total <= 0) return;

        // Throttle progress reports: report every ~0.2% or at least every step for small sets
        var step = total / 500 + 1;
        if (completed % step == 0 || completed == total)
        {
            var progressVal = 100.0 * completed / total;
            progress.Report(progressVal);
        }
    }
}
