using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Dupples_finder_UI.Services.Interfaces;

public interface ILoadingOperations
{
    bool GetAllPaths(out IEnumerable<string> paths, string rootFolder, bool includeSubfolders = true);

    /// <summary>
    /// Shows a folder browser dialog and returns the selected path, or null if cancelled.
    /// Must be called on the UI thread.
    /// </summary>
    string ShowFolderDialog();

    /// <summary>
    /// Scans a directory for image files. Safe to call from a background thread.
    /// Reports discovered file count via progress callback.
    /// </summary>
    List<string> ScanImageFiles(string rootFolder, bool includeSubfolders, IProgress<int> progress = null, CancellationToken ct = default);

    /// <summary>
    /// Scans a directory for image files, returning FileInfo objects with pre-fetched metadata.
    /// Much faster on network shares: directory enumeration populates size/timestamps
    /// in a single call per directory, avoiding per-file stat round-trips.
    /// </summary>
    List<FileInfo> ScanImageFileInfos(string rootFolder, bool includeSubfolders, IProgress<int> progress = null, CancellationToken ct = default);
}