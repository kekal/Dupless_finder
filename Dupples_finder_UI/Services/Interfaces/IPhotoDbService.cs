using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dupples_finder_UI.Data.Entities;

namespace Dupples_finder_UI.Services.Interfaces;

public interface IPhotoDbService : IDisposable
{
    bool IsAvailable { get; }
    Task<Photo> CachePhotoHashAsync(string filePath, byte[] descriptors, int rows, int cols, long fileSize, DateTime lastModifiedUtc);
    Task<Photo> CacheThumbnailAsync(long fileSize, DateTime lastModifiedUtc, string filePath, byte[] thumbnail);
    Task<Photo> GetCachedPhotoAsync(string filePath);
    Task<Photo> GetCachedPhotoByFingerprintAsync(long fileSize, DateTime lastModifiedUtc);
    Task<IList<SimilarityResult>> GetCachedResultsAsync();
    Task InitializeAsync(string dbPath = null);
    Task StoreSimilarityAsync(int photo1Id, int photo2Id, double score);

    /// <summary>
    /// Pre-loads all cached thumbnails into memory for fast O(1) lookups.
    /// Call before the loading pipeline to avoid per-image DB queries.
    /// </summary>
    Task PreloadThumbnailCacheAsync();

    /// <summary>
    /// Fast in-memory lookup for cached thumbnail bytes. No DB gate involved.
    /// Returns true if a cached thumbnail was found.
    /// </summary>
    bool TryGetCachedThumbnail(long fileSize, DateTime lastModifiedUtc, out byte[] thumbnail);

    /// <summary>
    /// Queues a thumbnail for batch DB write. Call <see cref="FlushThumbnailQueueAsync"/> to persist.
    /// </summary>
    void QueueThumbnailForCache(long fileSize, DateTime lastModifiedUtc, string filePath, byte[] thumbnail);

    /// <summary>
    /// Persists all queued thumbnails to DB in batches. Call after the loading pipeline completes.
    /// </summary>
    Task FlushThumbnailQueueAsync();

    /// <summary>
    /// Releases the in-memory thumbnail cache to free memory after loading is complete.
    /// </summary>
    void ClearThumbnailCache();

    /// <summary>
    /// True after <see cref="PreloadThumbnailCacheAsync"/> has completed.
    /// When true, <see cref="TryGetCachedThumbnail"/> is the authoritative source —
    /// a miss in memory means a miss in DB, so callers can skip the DB round-trip
    /// and its SemaphoreSlim(1,1) gate entirely.
    /// </summary>
    bool IsCachePreloaded { get; }
}