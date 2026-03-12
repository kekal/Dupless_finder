using Microsoft.JSInterop;

namespace DuplessFinder.Web.Services;

public class CacheService : ICacheService
{
    private readonly IJSRuntime _js;
    private readonly ILogger<CacheService> _logger;
    private IJSObjectReference? _module;
    private readonly SemaphoreSlim _moduleLock = new(1, 1);
    private bool _disposed;

    public CacheService(IJSRuntime js, ILogger<CacheService> logger)
    {
        _js = js;
        _logger = logger;
    }

    private async Task<IJSObjectReference> GetModuleAsync()
    {
        if (_module is not null) return _module;
        await _moduleLock.WaitAsync();
        try { return _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/indexeddb-interop.js"); }
        finally { _moduleLock.Release(); }
    }

    public async Task InitializeAsync()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CacheService));

        try
        {
            var mod = await GetModuleAsync();
            await mod.InvokeVoidAsync("openDatabase");
            await mod.InvokeVoidAsync("requestPersistentStorage");
            _logger.LogInformation("CacheService initialized (IndexedDB).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CacheService InitializeAsync failed: {Message}", ex.Message);
        }
    }

    public async Task<byte[]?> GetThumbnailAsync(string fingerprint)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CacheService));

        try
        {
            var mod = await GetModuleAsync();
            var bytes = await mod.InvokeAsync<byte[]?>("getThumbnail", fingerprint);
            return bytes is { Length: > 0 } ? bytes : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetThumbnailAsync failed for '{Fingerprint}': {Message}", fingerprint, ex.Message);
            return null;
        }
    }

    public async Task PutThumbnailAsync(string fingerprint, byte[] jpegBytes)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CacheService));

        try
        {
            var mod = await GetModuleAsync();
            await mod.InvokeVoidAsync("putThumbnail", fingerprint, jpegBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PutThumbnailAsync failed for '{Fingerprint}': {Message}", fingerprint, ex.Message);
        }
    }

    public async Task<SiftDescriptorData?> GetSiftDescriptorAsync(string fingerprint)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CacheService));

        try
        {
            var mod = await GetModuleAsync();
            var result = await mod.InvokeAsync<SiftDescriptorData?>("getSiftDescriptor", fingerprint);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetSiftDescriptorAsync failed for '{Fingerprint}': {Message}", fingerprint, ex.Message);
            return null;
        }
    }

    public async Task PutSiftDescriptorAsync(string fingerprint, byte[] data, int rows, int cols)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CacheService));

        try
        {
            var mod = await GetModuleAsync();
            await mod.InvokeVoidAsync("putSiftDescriptor", fingerprint, data, rows, cols);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PutSiftDescriptorAsync failed for '{Fingerprint}': {Message}", fingerprint, ex.Message);
        }
    }

    public async Task<List<SimilarityEntry>> GetAllSimilarityResultsAsync()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CacheService));

        try
        {
            var mod = await GetModuleAsync();
            var results = await mod.InvokeAsync<List<SimilarityEntry>?>("getAllSimilarityResults");
            return results ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetAllSimilarityResultsAsync failed: {Message}", ex.Message);
            return [];
        }
    }

    public async Task StoreSimilarityBatchAsync(IEnumerable<SimilarityEntry> entries)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CacheService));

        try
        {
            var mod = await GetModuleAsync();
            var batch = entries.Select(e => new { fingerprint1 = e.Fingerprint1, fingerprint2 = e.Fingerprint2, score = e.Score }).ToArray();
            await mod.InvokeVoidAsync("storeSimilarityBatch", (object)batch);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StoreSimilarityBatchAsync failed: {Message}", ex.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _moduleLock.WaitAsync();
        try
        {
            if (_module is not null)
            {
                try
                {
                    await _module.DisposeAsync();
                }
                catch (JSDisconnectedException)
                {
                    // Circuit disconnected, safe to ignore
                }

                _module = null;
            }
        }
        finally
        {
            _moduleLock.Release();
            _moduleLock.Dispose();
        }
    }
}
