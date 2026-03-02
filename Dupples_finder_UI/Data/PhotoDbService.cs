using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dupples_finder_UI.Data.Entities;
using Dupples_finder_UI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Dupples_finder_UI.Data
{
    /// <summary>
    /// Service for caching photo SIFT hashes, thumbnails, and similarity results in SQLite.
    /// All operations are designed to fail gracefully — if the DB is unavailable,
    /// the application continues without caching.
    /// Thread-safe: all DbContext access is serialized through a semaphore.
    /// </summary>
    public class PhotoDbService : IPhotoDbService
    {
        private DuplessDbContext _context;
        private bool _isAvailable;
        private bool _disposed;
        private readonly SemaphoreSlim _gate = new(1, 1);

        /// <summary>
        /// Indicates whether the database is initialized and available for use.
        /// </summary>
        public bool IsAvailable => _isAvailable;

        /// <summary>
        /// Initializes the database connection and ensures the schema exists.
        /// If initialization fails, the service remains available but non-functional.
        /// </summary>
        public async Task InitializeAsync(string dbPath = null)
        {
            dbPath ??= "dupless_cache.db";
            try
            {
                _context = new DuplessDbContext(dbPath);
                await _context.Database.EnsureCreatedAsync();

                // Validate schema: if the DB was created by an older version it may
                // be missing columns (e.g. LastModifiedUtc).  A quick probe detects
                // this; if the schema is stale we drop and recreate the file.
                try
                {
                    await _context.Photos.Select(p => p.LastModifiedUtc).FirstOrDefaultAsync();
                }
                catch (Microsoft.Data.Sqlite.SqliteException)
                {
                    Trace.WriteLine("DB schema outdated — recreating cache database.");
                    await _context.DisposeAsync();
                    // SQLite pools connections; clear them so the file handle is released.
                    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                    File.Delete(dbPath);
                    _context = new DuplessDbContext(dbPath);
                    await _context.Database.EnsureCreatedAsync();
                }

                _isAvailable = true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"DB init failed: {ex.Message}. Running without cache.");
                _isAvailable = false;
                if (_context != null)
                {
                    await _context.DisposeAsync();
                }

                _context = null;
            }
        }

        /// <summary>
        /// Retrieves a cached photo record by file path.
        /// Returns null if DB is unavailable or the photo is not cached.
        /// </summary>
        public async Task<Photo> GetCachedPhotoAsync(string filePath)
        {
            if (!_isAvailable)
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
                Trace.WriteLine($"DB read error: {ex.Message}");
                return null;
            }
            finally { _gate.Release(); }
        }

        /// <summary>
        /// Retrieves a cached photo record by its fingerprint (FileSize + LastModifiedUtc).
        /// This is the preferred lookup method — it finds cached data even when a file
        /// has been moved or renamed, as long as size and timestamp match.
        /// Returns null if DB is unavailable or no matching photo is cached.
        /// </summary>
        public async Task<Photo> GetCachedPhotoByFingerprintAsync(long fileSize, DateTime lastModifiedUtc)
        {
            if (!_isAvailable)
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
                Trace.WriteLine($"DB fingerprint read error: {ex.Message}");
                return null;
            }
            finally { _gate.Release(); }
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
            if (!_isAvailable)
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
                Trace.WriteLine($"DB write error: {ex.Message}");
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
            if (!_isAvailable)
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
                Trace.WriteLine($"DB thumbnail write error: {ex.Message}");
                return null;
            }
            finally { _gate.Release(); }
        }

        /// <summary>
        /// Stores or updates a similarity comparison result between two photos.
        /// Photo IDs are normalized so Photo1Id is always the smaller ID.
        /// </summary>
        public async Task StoreSimilarityAsync(int photo1Id, int photo2Id, double score)
        {
            if (!_isAvailable)
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
                Trace.WriteLine($"DB similarity write error: {ex.Message}");
            }
            finally { _gate.Release(); }
        }

        /// <summary>
        /// Retrieves all cached similarity results with their associated photo data,
        /// ordered by score ascending.
        /// </summary>
        public async Task<IList<SimilarityResult>> GetCachedResultsAsync()
        {
            if (!_isAvailable)
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
                Trace.WriteLine($"DB results read error: {ex.Message}");
                return new List<SimilarityResult>();
            }
            finally { _gate.Release(); }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _context?.Dispose();
                _gate.Dispose();
                _disposed = true;
            }
        }
    }
}
