namespace DuplessFinder.Web.Services;

public record SiftResult(int Rows, int Cols, byte[] DescriptorData);

public interface IOpenCvService : IAsyncDisposable
{
    Task EnsureLoadedAsync();
    Task<SiftResult?> ComputeSiftAsync(byte[] imageBytes, string fingerprint);
    Task<double> MatchPairAsync(SiftResult desc1, SiftResult desc2);
    Task<byte[]> GenerateThumbnailAsync(byte[] imageBytes, int maxSize);
}
