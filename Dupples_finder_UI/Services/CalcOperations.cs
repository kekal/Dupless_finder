using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dupples_finder_UI.DTO;
using Dupples_finder_UI.Services.Interfaces;
using OpenCvSharp;
using OpenCvSharp.Features2D;
using OpenCvSharp.Flann;

namespace Dupples_finder_UI.Services;

public class CalcOperations : ICalcOperations
{
    private readonly IMatSerializer _matSerializer;
    private int[] _completedIterations;
    private int[] _iterations;
    private IProgress<double> _currentProgress;

    public CalcOperations(IMatSerializer matSerializer)
    {
        _matSerializer = matSerializer;
    }

    /// <summary>
    /// Computes SIFT hashes for all images, using fingerprint-based DB cache.
    /// Cache lookups use the ImageInfo's FileSize + LastModifiedUtc (fingerprint)
    /// instead of file path, so moved/renamed files still get cache hits.
    /// </summary>
    public ConcurrentDictionary<string, Mat> CalcSiftHashes(IEnumerable<ImageInfo> infos, IPhotoDbService dbService, IProgress<double> progress, out Task result, int thumbSize = 256, CancellationToken ct = default)
    {
        PerfLogger.Log("CalcSiftHashes started");

        EnablePublishingProgress(progress);

        var hashesDict = new ConcurrentDictionary<string, Mat>();

        var tasks = new List<Task>();
        foreach (var info in infos)
        {
            var task = Task.Run(async () =>
            {
                ct.ThrowIfCancellationRequested();
                Thread.CurrentThread.Priority = ThreadPriority.Lowest;

                // Try to load cached SIFT descriptors from DB using fingerprint
                if (dbService is { IsAvailable: true })
                {
                    try
                    {
                        var cached = await dbService.GetCachedPhotoByFingerprintAsync(
                            info.FileSize, info.LastModifiedUtc);
                        if (cached?.SiftDescriptors is { Length: > 0 })
                        {
                            var cachedMat = _matSerializer.Deserialize(cached.SiftDescriptors, cached.DescriptorRows, cached.DescriptorCols);
                            if (cachedMat != null)
                            {
                                hashesDict.TryAdd(info.FilePath, cachedMat);
                                UpdateIterationsCount();
                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        PerfLogger.Log($"Cache lookup failed for {info.FileName}: {ex.Message}");
                    }
                }

                using (var siftPoints = SIFT.Create())
                {
                    var descriptors = new Mat();

                    double scale = Math.Min((float) thumbSize / info.StoredMat.Width, (float) thumbSize / info.StoredMat.Height);
                    var resized = info.StoredMat.Resize(new Size(0, 0), scale, scale, InterpolationFlags.Area);
                    siftPoints.DetectAndCompute(resized, null, out _, descriptors);
                    resized.Release();

                    if (!ValidateDescriptor(descriptors))
                    {
                        descriptors.Release();
                        UpdateIterationsCount();
                        return; // no keypoints — skip this image
                    }

                    hashesDict.TryAdd(info.FilePath, descriptors);

                    // Cache the computed SIFT descriptors in DB using fingerprint
                    if (dbService is { IsAvailable: true })
                    {
                        try
                        {
                            var serialized = _matSerializer.Serialize(descriptors);
                            await dbService.CachePhotoHashAsync(
                                info.FilePath,
                                serialized,
                                descriptors.Rows,
                                descriptors.Cols,
                                info.FileSize,
                                info.LastModifiedUtc);
                        }
                        catch (Exception ex)
                        {
                            PerfLogger.Log($"Cache store failed for {info.FileName}: {ex.Message}");
                        }
                    }
                }

                UpdateIterationsCount();
            });

            tasks.Add(task);
        }

        SetProgressIterationsScope(tasks);

        result = Task.WhenAll(tasks.ToArray()).ContinueWith(_ => DisablePublishingProgress(), CancellationToken.None);

        return hashesDict;
    }

    public IEnumerable<PairSimilarityInfo> CreateMatchCollection(IDictionary<string, Mat> hashDict, IProgress<double> progress, CancellationToken ct = default)
    {
        EnablePublishingProgress(progress);
        var similarities = new ConcurrentBag<PairSimilarityInfo>();
        var hashes = hashDict.ToArray();
        // Total unique pairs = n*(n-1)/2
        var pairCount = hashes.Length * (hashes.Length - 1) / 2;
        SetProgressIterationsScope(Math.Max(pairCount, 1));
        for (var j = 0; j < hashes.Length; j++)
        {
            ct.ThrowIfCancellationRequested();
            var jCopy = j;

            Parallel.For(jCopy + 1, hashes.Length, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, i1 =>
            {
                Thread.CurrentThread.Priority = ThreadPriority.Lowest;

                var similarity = CalcSimilarity((hashes[jCopy], hashes[i1]));
                similarities.Add(new PairSimilarityInfo(hashes[jCopy], hashes[i1], similarity));

                UpdateIterationsCount();
            });
        }

        DisablePublishingProgress();
        return similarities.OrderBy(o => o.Match);
    }

    private static double CalcSimilarity((KeyValuePair<string, Mat>, KeyValuePair<string, Mat>) pairOfHashes)
    {
        Thread.CurrentThread.Priority = ThreadPriority.Lowest;

        var d1 = pairOfHashes.Item1.Value;
        var d2 = pairOfHashes.Item2.Value;

        // KnnMatch(k=2) needs >= 2 rows in each descriptor, and cols must match.
        if (d1.Rows < 2 || d2.Rows < 2 || d1.Cols != d2.Cols)
        {
            return double.MaxValue;
        }

        // KDTree is the correct index for SIFT's float (CV_32F) descriptors.
        using var matcher = new FlannBasedMatcher(new KDTreeIndexParams(5), new SearchParams());
        DMatch[][] matches;
        try
        {
            matches = matcher.KnnMatch(d1, d2, 2);
        }
        catch (Exception ex)
        {
            PerfLogger.Log($"Matcher failed: {ex.Message}");
            return double.MaxValue;
        }

        if (matches.Length < 2)
        {
            return double.MaxValue;
        }

        const double ratioThresh = 0.7;
        var goodMatches = matches
            .Where(match => match.Length > 1 && match[0].Distance < ratioThresh * match[1].Distance)
            .Select(match => match[0])
            .ToList();

        return goodMatches.Count > 0 ? 1000.0 / goodMatches.Count : double.MaxValue;
    }

    private static bool ValidateDescriptor(Mat descriptors)
    {
        return descriptors.Width > 0 && descriptors.Height > 0;
    }

    private void DisablePublishingProgress()
    {
        _currentProgress = null;
    }

    private void EnablePublishingProgress(IProgress<double> progress)
    {
        _iterations = [-1];
        _completedIterations = [-1];
        _currentProgress = progress;
        progress?.Report(0);
    }

    private void SetProgressIterationsScope(ICollection elements)
    {
        SetProgressIterationsScope(elements.Count);
    }

    private void SetProgressIterationsScope(int count)
    {
        _iterations[0] = count;
    }

    private void UpdateIterationsCount()
    {
        Interlocked.Increment(ref _completedIterations[0]);

        var step = _iterations[0] / 500 + 1;
        var progressVal = 100.0 * _completedIterations[0] / _iterations[0];
        if (_completedIterations[0] % step == 0)
        {
            _currentProgress?.Report(progressVal);
        }
    }
}