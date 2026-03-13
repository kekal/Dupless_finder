using DuplessFinder.Web.Services;

namespace DuplessFinder.Web.Models;

public class ImageInfo : IDisposable
{
    private string? _thumbnailDataUrl;
    private bool _disposed;

    public ImageInfo(FileEntry entry)
    {
        Path = entry.Path;
        Name = entry.Name;
        FileSize = entry.Size;
        LastModified = entry.LastModified;
    }

    public string Path { get; }
    public string Name { get; }
    public long FileSize { get; }
    public long LastModified { get; }

    /// <summary>
    /// Lightweight identity fingerprint combining FileSize and LastModified.
    /// Used as cache key in IndexedDB.
    /// </summary>
    public string Fingerprint => $"{FileSize}_{LastModified}";

    public string? ThumbnailDataUrl
    {
        get => _thumbnailDataUrl;
        set
        {
            _thumbnailDataUrl = value;
            IsThumbnailLoaded = value != null;
        }
    }

    public bool IsThumbnailLoaded { get; private set; }

    public async Task LoadThumbnailAsync(
        ICacheService cacheService,
        IFileAccessService fileAccess,
        IOpenCvService? opencvService = null,
        int maxSize = 200,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Step 1: Check IndexedDB cache
        var cached = await cacheService.GetThumbnailAsync(Fingerprint);
        if (cached is { Length: > 0 })
        {
            ThumbnailDataUrl = $"data:image/jpeg;base64,{Convert.ToBase64String(cached)}";
            return;
        }

        ct.ThrowIfCancellationRequested();

        // Step 2: Generate thumbnail via canvas or opencv
        try
        {
            var fileBytes = await fileAccess.ReadFileBytesAsync(Path);

            ct.ThrowIfCancellationRequested();

            byte[]? thumbBytes = null;

            if (opencvService != null)
            {
                thumbBytes = await opencvService.GenerateThumbnailAsync(fileBytes, maxSize, Name);
            }

            ct.ThrowIfCancellationRequested();

            if (thumbBytes is { Length: > 0 })
            {
                ThumbnailDataUrl = $"data:image/jpeg;base64,{Convert.ToBase64String(thumbBytes)}";
                // Queue for cache
                await cacheService.PutThumbnailAsync(Fingerprint, thumbBytes);
            }
            else
            {
                // Fallback: create object URL directly
                // Note: blob: URLs created here must be revoked by the caller (async JS interop required)
                var objectUrl = await fileAccess.CreateObjectUrlAsync(Path);
                ThumbnailDataUrl = objectUrl;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Thumbnail load failed for '{Name}': {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            // Note: if IsBlobUrl is true, the caller must revoke the blob URL
            // via async JS interop before disposing, as Dispose cannot be async.
            _thumbnailDataUrl = null;
            _disposed = true;
        }
    }
}
