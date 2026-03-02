using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dupples_finder_UI.Data.Entities;

namespace Dupples_finder_UI.Services.Interfaces
{
    public interface IPhotoDbService : IDisposable
    {
        bool IsAvailable { get; }
        Task InitializeAsync(string dbPath = null);
        Task<Photo> GetCachedPhotoAsync(string filePath);
        Task<Photo> GetCachedPhotoByFingerprintAsync(long fileSize, DateTime lastModifiedUtc);
        Task<Photo> CachePhotoHashAsync(string filePath, byte[] descriptors, int rows, int cols, long fileSize, DateTime lastModifiedUtc);
        Task<Photo> CacheThumbnailAsync(long fileSize, DateTime lastModifiedUtc, string filePath, byte[] thumbnail);
        Task StoreSimilarityAsync(int photo1Id, int photo2Id, double score);
        Task<IList<SimilarityResult>> GetCachedResultsAsync();
    }
}
