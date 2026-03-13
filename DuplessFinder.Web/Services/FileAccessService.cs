using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DuplessFinder.Web.Services;

public class FileAccessService : IFileAccessService
{
    private static readonly string CacheBuster = Guid.NewGuid().ToString("N")[..8];

    private readonly IJSRuntime _js;
    private readonly ILogger<FileAccessService> _logger;
    private IJSObjectReference? _module;
    private readonly SemaphoreSlim _moduleLock = new(1, 1);
    private bool _disposed;

    public FileAccessService(IJSRuntime js, ILogger<FileAccessService> logger)
    {
        _js = js;
        _logger = logger;
    }

    private async Task<IJSObjectReference> GetModuleAsync()
    {
        if (_module is not null) return _module;
        await _moduleLock.WaitAsync();
        try { return _module ??= await _js.InvokeAsync<IJSObjectReference>("import", $"./js/file-access-interop.js?v={CacheBuster}"); }
        finally { _moduleLock.Release(); }
    }

    private static void ValidateRelativePath(string relativePath)
    {
        if (relativePath.Contains(".."))
            throw new ArgumentException("Path traversal not allowed", nameof(relativePath));
    }

    public async Task<string?> PickDirectoryAsync()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FileAccessService));

        try
        {
            var mod = await GetModuleAsync();
            return await mod.InvokeAsync<string?>("pickDirectory");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PickDirectoryAsync failed: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<List<FileEntry>> PickFilesAsync(ElementReference inputElement)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FileAccessService));

        try
        {
            var mod = await GetModuleAsync();
            var entries = await mod.InvokeAsync<List<FileEntry>?>("pickFiles", inputElement);
            return entries ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PickFilesAsync failed: {Message}", ex.Message);
            return [];
        }
    }

    public async Task<bool> EnsureDirectoryAccessAsync()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FileAccessService));

        try
        {
            var mod = await GetModuleAsync();
            return await mod.InvokeAsync<bool>("ensureDirectoryAccess");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EnsureDirectoryAccessAsync failed: {Message}", ex.Message);
            return false;
        }
    }

    public async Task<string?> ResolvePathByFingerprintAsync(string fingerprint, string fileName)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FileAccessService));

        try
        {
            var mod = await GetModuleAsync();
            return await mod.InvokeAsync<string?>("resolvePathByFingerprint", fingerprint, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ResolvePathByFingerprintAsync failed: {Message}", ex.Message);
            return null;
        }
    }

    public async Task ClickElementAsync(ElementReference element)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FileAccessService));

        var mod = await GetModuleAsync();
        await mod.InvokeVoidAsync("clickElement", element);
    }

    public async Task<List<FileEntry>> ScanImagesAsync(bool includeSubfolders)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FileAccessService));

        try
        {
            var mod = await GetModuleAsync();
            var entries = await mod.InvokeAsync<List<FileEntry>?>("scanImages", includeSubfolders);
            return entries ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ScanImagesAsync failed: {Message}", ex.Message);
            return [];
        }
    }

    public async Task<byte[]> ReadFileBytesAsync(string relativePath)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FileAccessService));
        ValidateRelativePath(relativePath);

        try
        {
            var mod = await GetModuleAsync();
            var bytes = await mod.InvokeAsync<byte[]>("readFileBytes", relativePath);
            return bytes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReadFileBytesAsync failed for '{RelativePath}': {Message}", relativePath, ex.Message);
            return [];
        }
    }

    public async Task<string> MoveToDeletedAsync(string relativePath)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FileAccessService));
        ValidateRelativePath(relativePath);

        try
        {
            var mod = await GetModuleAsync();
            return await mod.InvokeAsync<string>("moveToDeleted", relativePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MoveToDeletedAsync failed for '{RelativePath}': {Message}", relativePath, ex.Message);
            throw;
        }
    }

    public async Task RestoreFromDeletedAsync(string originalPath, string deletedName)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FileAccessService));
        ValidateRelativePath(originalPath);

        try
        {
            var mod = await GetModuleAsync();
            await mod.InvokeVoidAsync("restoreFromDeleted", originalPath, deletedName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RestoreFromDeletedAsync failed: {Message}", ex.Message);
            throw;
        }
    }

    public async Task<string> CreateObjectUrlAsync(string relativePath)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FileAccessService));
        ValidateRelativePath(relativePath);

        try
        {
            var mod = await GetModuleAsync();
            return await mod.InvokeAsync<string>("createObjectUrl", relativePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateObjectUrlAsync failed for '{RelativePath}': {Message}", relativePath, ex.Message);
            throw;
        }
    }

    public async Task RevokeObjectUrlAsync(string url)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FileAccessService));

        try
        {
            var mod = await GetModuleAsync();
            await mod.InvokeVoidAsync("revokeObjectUrl", url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RevokeObjectUrlAsync failed: {Message}", ex.Message);
        }
    }

    public async Task<bool> IsFileSystemAccessSupportedAsync()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FileAccessService));

        try
        {
            var mod = await GetModuleAsync();
            return await mod.InvokeAsync<bool>("isFileSystemAccessSupported");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IsFileSystemAccessSupportedAsync failed: {Message}", ex.Message);
            return false;
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
