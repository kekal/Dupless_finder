using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dupples_finder_UI.Data.Entities;
using Dupples_finder_UI.Services;
using Dupples_finder_UI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Dupples_finder_UI.Data;

/// <summary>
/// Service for caching photo SIFT hashes, thumbnails, and similarity results in SQLite.
/// All operations are designed to fail gracefully — if the DB is unavailable,
/// the application continues without caching.
/// Thread-safe: all DbContext access is serialized through a semaphore.
/// </summary>
public class PhotoDbService : IPhotoDbService
{
    private bool _disposed;
    private DuplessDbContext _context;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>In-memory thumbnail cache: fingerprint key -> JPEG bytes.</summary>
    private ConcurrentDictionary<string, byte[]> _thumbnailCache;

    /// <summary>Queue for batch-writing new thumbnails to DB.</summary>
    private readonly ConcurrentQueue<(long fileSize, DateTime lastModifiedUtc, string filePath, byte[] thumbnail)> _thumbnailQueue = new();

    public bool IsAvailable { get; private set; }

    public bool IsCachePreloaded => _thumbnailCache != null;

    public void Dispose()
    {
        if (!_disposed)
        {
            _context?.Dispose();
            _gate.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Stores or updates a photo's SIFT descriptor data in the cache.
    /// Uses the fingerprint (FileSize + LastModifiedUtc) as the identity key
    /// for upsert logic — if a photo with the same fingerprint exists, its
    /// record is updated rather than creating a duplicate.
    /// Returns the saved Photo entity, or null if the operation fails.
    /// </summary>
    public async Task<Photo> CachePhotoHashAsync(
        string filePath,
        byte[] descriptors,
        int rows,
        int cols,
        long fileSize,
        DateTime lastModifiedUtc)
    {
        if (!IsAvailable)
        {
            return null;
        }

        await _gate.WaitAsync();
        try
        {
            // Look up by fingerprint first (primary identity).
            var existing = await _context.Photos
                .FirstOrDefaultAsync(p => p.FileSize == fileSize
                                          && p.LastModifiedUtc == lastModifiedUtc);

            if (existing != null)
            {
                existing.FilePath = filePath;
                existing.SiftDescriptors = descriptors;
                existing.DescriptorRows = rows;
                existing.DescriptorCols = cols;
                existing.HashDate = DateTime.UtcNow;
            }
            else
            {
                existing = new Photo
                {
                    FilePath = filePath,
                    SiftDescriptors = descriptors,
                    DescriptorRows = rows,
                    DescriptorCols = cols,
                    FileSize = fileSize,
                    LastModifiedUtc = lastModifiedUtc,
                    HashDate = DateTime.UtcNow
                };
                _context.Photos.Add(existing);
            }

            await _context.SaveChangesAsync();
            return existing;
        }
        catch (Exception ex)
        {
            PerfLogger.Log($"DB write error: {ex.Message}");
            return null;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Stores or updates a JPEG thumbnail for a photo identified by its fingerprint.
    /// If a record with the given fingerprint already exists, the thumbnail (and
    /// optionally the file path) is updated.  Otherwise, a new record is created
    /// with only the thumbnail data populated; SIFT descriptors can be filled later.
    /// Returns the saved Photo entity, or null if the operation fails.
    /// </summary>
    public async Task<Photo> CacheThumbnailAsync(
        long fileSize,
        DateTime lastModifiedUtc,
        string filePath,
        byte[] thumbnail)
    {
        if (!IsAvailable)
        {
            return null;
        }

        await _gate.WaitAsync();
        try
        {
            var existing = await _context.Photos
                .FirstOrDefaultAsync(p => p.FileSize == fileSize
                                          && p.LastModifiedUtc == lastModifiedUtc);

            if (existing != null)
            {
                existing.Thumbnail = thumbnail;
                existing.FilePath = filePath;
            }
            else
            {
                existing = new Photo
                {
                    FilePath = filePath,
                    FileSize = fileSize,
                    LastModifiedUtc = lastModifiedUtc,
                    Thumbnail = thumbnail,
                    HashDate = DateTime.UtcNow
                };
                _context.Photos.Add(existing);
            }

            await _context.SaveChangesAsync();
            return existing;
        }
        catch (Exception ex)
        {
            PerfLogger.Log($"DB thumbnail write error: {ex.Message}");
            return null;
        }
        finally { _gate.Release(); }
    }

    public async Task<Photo> GetCachedPhotoAsync(string filePath)
    {
        if (!IsAvailable)
        {
            return null;
        }

        await _gate.WaitAsync();
        try
        {
            return await _context.Photos
                .FirstOrDefaultAsync(p => p.FilePath == filePath);
        }
        catch (Exception ex)
        {
            PerfLogger.Log($"DB read error: {ex.Message}");
            return null;
        }
        finally { _gate.Release(); }
    }

    public async Task<Photo> GetCachedPhotoByFingerprintAsync(long fileSize, DateTime lastModifiedUtc)
    {
        if (!IsAvailable)
        {
            return null;
        }

        await _gate.WaitAsync();
        try
        {
            return await _context.Photos
                .FirstOrDefaultAsync(p => p.FileSize == fileSize
                                          && p.LastModifiedUtc == lastModifiedUtc);
        }
        catch (Exception ex)
        {
            PerfLogger.Log($"DB fingerprint read error: {ex.Message}");
            return null;
        }
        finally { _gate.Release(); }
    }

    public async Task<IList<SimilarityResult>> GetCachedResultsAsync()
    {
        if (!IsAvailable)
        {
            return new List<SimilarityResult>();
        }

        await _gate.WaitAsync();
        try
        {
            return await _context.SimilarityResults
                .Include(r => r.Photo1)
                .Include(r => r.Photo2)
                .OrderBy(r => r.Score)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            PerfLogger.Log($"DB results read error: {ex.Message}");
            return new List<SimilarityResult>();
        }
        finally { _gate.Release(); }
    }

    public async Task InitializeAsync(string dbPath = null)
    {
        dbPath ??= "dupless_cache.db";
        try
        {
            _context = new DuplessDbContext(dbPath);
            await _context.Database.EnsureCreatedAsync();


            try
            {
                await _context.Photos.Select(p => p.LastModifiedUtc).FirstOrDefaultAsync();
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                PerfLogger.Log("DB schema outdated — recreating cache database.");
                await _context.DisposeAsync();
                
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                File.Delete(dbPath);
                _context = new DuplessDbContext(dbPath);
                await _context.Database.EnsureCreatedAsync();
            }

            IsAvailable = true;
        }
        catch (Exception ex)
        {
            PerfLogger.Log($"DB init failed: {ex.Message}. Running without cache.");
            IsAvailable = false;
            if (_context != null)
            {
                await _context.DisposeAsync();
            }

            _context = null;
        }
    }

    public async Task PreloadThumbnailCacheAsync()
    {
        if (!IsAvailable)
        {
            _thumbnailCache = new ConcurrentDictionary<string, byte[]>();
            return;
        }

        await _gate.WaitAsync();
        try
        {
            var photos = await _context.Photos
                .Where(p => p.Thumbnail != null)
                .Select(p => new { p.FileSize, p.LastModifiedUtc, p.Thumbnail })
                .ToListAsync();

            _thumbnailCache = new ConcurrentDictionary<string, byte[]>(
                photos.ToDictionary(
                    p => $"{p.FileSize}_{p.LastModifiedUtc.Ticks}",
                    p => p.Thumbnail));

            PerfLogger.Log($"Preloaded {_thumbnailCache.Count} cached thumbnails.");
        }
        catch (Exception ex)
        {
            PerfLogger.Log($"Thumbnail cache preload failed: {ex.Message}");
            _thumbnailCache = new ConcurrentDictionary<string, byte[]>();
        }
        finally { _gate.Release(); }
    }

    public bool TryGetCachedThumbnail(long fileSize, DateTime lastModifiedUtc, out byte[] thumbnail)
    {
        thumbnail = null;
        if (_thumbnailCache == null)
        {
            return false;
        }

        var key = $"{fileSize}_{lastModifiedUtc.Ticks}";
        return _thumbnailCache.TryGetValue(key, out thumbnail) && thumbnail is { Length: > 0 };
    }

    public void QueueThumbnailForCache(long fileSize, DateTime lastModifiedUtc, string filePath, byte[] thumbnail)
    {
        if (!IsAvailable || thumbnail == null || thumbnail.Length == 0)
        {
            return;
        }

        _thumbnailQueue.Enqueue((fileSize, lastModifiedUtc, filePath, thumbnail));

        // Also add to in-memory cache so subsequent lookups hit immediately
        var key = $"{fileSize}_{lastModifiedUtc.Ticks}";
        _thumbnailCache?.TryAdd(key, thumbnail);
    }

    public async Task FlushThumbnailQueueAsync()
    {
        if (!IsAvailable || _thumbnailQueue.IsEmpty)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            var batch = new List<(long fileSize, DateTime lastModifiedUtc, string filePath, byte[] thumbnail)>();
            while (_thumbnailQueue.TryDequeue(out var item))
            {
                batch.Add(item);
            }

            // Process in chunks to avoid huge single transactions
            const int chunkSize = 500;
            for (var i = 0; i < batch.Count; i += chunkSize)
            {
                var chunk = batch.Skip(i).Take(chunkSize);
                foreach (var item in chunk)
                {
                    var existing = await _context.Photos
                        .FirstOrDefaultAsync(p => p.FileSize == item.fileSize
                                                  && p.LastModifiedUtc == item.lastModifiedUtc);
                    if (existing != null)
                    {
                        existing.Thumbnail = item.thumbnail;
                        existing.FilePath = item.filePath;
                    }
                    else
                    {
                        _context.Photos.Add(new Photo
                        {
                            FilePath = item.filePath,
                            FileSize = item.fileSize,
                            LastModifiedUtc = item.lastModifiedUtc,
                            Thumbnail = item.thumbnail,
                            HashDate = DateTime.UtcNow
                        });
                    }
                }

                await _context.SaveChangesAsync();
            }

            PerfLogger.Log($"Flushed {batch.Count} thumbnails to DB.");
        }
        catch (Exception ex)
        {
            PerfLogger.Log($"Thumbnail flush failed: {ex.Message}");
        }
        finally { _gate.Release(); }
    }

    public void ClearThumbnailCache()
    {
        _thumbnailCache?.Clear();
        _thumbnailCache = null;
    }

    /// <summary>
    /// Stores or updates a similarity comparison result between two photos.
    /// Photo IDs are normalized so Photo1Id is always the smaller ID.
    /// </summary>
    public async Task StoreSimilarityAsync(int photo1Id, int photo2Id, double score)
    {
        if (!IsAvailable)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            var (id1, id2) = photo1Id < photo2Id
                ? (photo1Id, photo2Id)
                : (photo2Id, photo1Id);

            var existing = await _context.SimilarityResults
                .FirstOrDefaultAsync(r => r.Photo1Id == id1 && r.Photo2Id == id2);

            if (existing != null)
            {
                existing.Score = score;
                existing.CompareDate = DateTime.UtcNow;
            }
            else
            {
                _context.SimilarityResults.Add(new SimilarityResult
                {
                    Photo1Id = id1,
                    Photo2Id = id2,
                    Score = score,
                    CompareDate = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            PerfLogger.Log($"DB similarity write error: {ex.Message}");
        }
        finally { _gate.Release(); }
    }
}