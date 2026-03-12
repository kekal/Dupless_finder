using Microsoft.JSInterop;

namespace DuplessFinder.Web.Services;

public class OpenCvService : IOpenCvService
{
    private readonly IJSRuntime _js;
    private readonly ILogger<OpenCvService> _logger;
    private IJSObjectReference? _module;
    private readonly SemaphoreSlim _moduleLock = new(1, 1);
    private bool _disposed;

    public OpenCvService(IJSRuntime js, ILogger<OpenCvService> logger)
    {
        _js = js;
        _logger = logger;
    }

    private async Task<IJSObjectReference> GetModuleAsync()
    {
        if (_module is not null) return _module;
        await _moduleLock.WaitAsync();
        try { return _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/opencv-interop.js"); }
        finally { _moduleLock.Release(); }
    }

    public async Task EnsureLoadedAsync()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(OpenCvService));

        try
        {
            var mod = await GetModuleAsync();
            await mod.InvokeVoidAsync("ensureLoaded");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EnsureLoadedAsync failed: {Message}", ex.Message);
            throw;
        }
    }

    public async Task<SiftResult?> ComputeSiftAsync(byte[] imageBytes, string fingerprint)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(OpenCvService));

        try
        {
            var mod = await GetModuleAsync();
            var result = await mod.InvokeAsync<SiftResult?>("computeSift", imageBytes);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ComputeSiftAsync failed for '{Fingerprint}': {Message}", fingerprint, ex.Message);
            return null;
        }
    }

    public async Task<double> MatchPairAsync(SiftResult desc1, SiftResult desc2)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(OpenCvService));

        try
        {
            var mod = await GetModuleAsync();
            var obj1 = new { rows = desc1.Rows, cols = desc1.Cols, data = desc1.DescriptorData };
            var obj2 = new { rows = desc2.Rows, cols = desc2.Cols, data = desc2.DescriptorData };
            var score = await mod.InvokeAsync<double>("matchPair", obj1, obj2);
            return score;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MatchPairAsync failed: {Message}", ex.Message);
            return double.MaxValue;
        }
    }

    public async Task<byte[]> GenerateThumbnailAsync(byte[] imageBytes, int maxSize)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(OpenCvService));

        try
        {
            var mod = await GetModuleAsync();
            var thumbBytes = await mod.InvokeAsync<byte[]>("generateThumbnail", imageBytes, maxSize);
            return thumbBytes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GenerateThumbnailAsync failed: {Message}", ex.Message);
            return [];
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
                    await _module.InvokeVoidAsync("clearDescriptorCache");
                }
                catch (JSDisconnectedException)
                {
                    // Circuit disconnected, safe to ignore
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "clearDescriptorCache failed during dispose: {Message}", ex.Message);
                }

                try
                {
                    await _module.InvokeVoidAsync("terminateWorkerPool");
                }
                catch (JSDisconnectedException)
                {
                    // Circuit disconnected, safe to ignore
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "terminateWorkerPool failed during dispose: {Message}", ex.Message);
                }

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
