using Microsoft.AspNetCore.Components;

namespace DuplessFinder.Web.Services;

public record FileEntry(string Path, string Name, long Size, long LastModified);

public interface IFileAccessService : IAsyncDisposable
{
    Task<string?> PickDirectoryAsync();
    Task<List<FileEntry>> PickFilesAsync(ElementReference inputElement);
    Task<bool> EnsureDirectoryAccessAsync();
    Task<string?> ResolvePathByFingerprintAsync(string fingerprint, string fileName);
    Task<List<FileEntry>> ScanImagesAsync(bool includeSubfolders);
    Task<byte[]> ReadFileBytesAsync(string relativePath);
    Task<string> MoveToDeletedAsync(string relativePath);
    Task RestoreFromDeletedAsync(string originalPath, string deletedName);
    Task<string> CreateObjectUrlAsync(string relativePath);
    Task RevokeObjectUrlAsync(string url);
    Task<bool> IsFileSystemAccessSupportedAsync();
    Task ClickElementAsync(ElementReference element);
}
