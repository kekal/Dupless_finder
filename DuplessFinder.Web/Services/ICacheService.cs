namespace DuplessFinder.Web.Services;

public record SiftDescriptorData(int Rows, int Cols, byte[] Data);

public record SimilarityEntry(string Fingerprint1, string Fingerprint2, double Score);

public interface ICacheService : IAsyncDisposable
{
    Task InitializeAsync();
    Task<byte[]?> GetThumbnailAsync(string fingerprint);
    Task PutThumbnailAsync(string fingerprint, byte[] jpegBytes);
    Task<SiftDescriptorData?> GetSiftDescriptorAsync(string fingerprint);
    Task PutSiftDescriptorAsync(string fingerprint, byte[] data, int rows, int cols);
    Task<List<SimilarityEntry>> GetAllSimilarityResultsAsync();
    Task StoreSimilarityBatchAsync(IEnumerable<SimilarityEntry> entries);
}
