using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Dupples_finder_UI.Services.Interfaces;

namespace Dupples_finder_UI.Services;

public class LoadingOperations : ILoadingOperations
{
    private static readonly string[] ImageExtensions = [".jpg", ".png", ".jpeg", ".bmp", ".tiff", ".tif", ".webp"];

    public bool GetAllPaths(out IEnumerable<string> paths, string rootFolder, bool includeSubfolders = true)
    {
        if (rootFolder == string.Empty)
        {
            using var fbd = new FolderBrowserDialog();
            fbd.ShowNewFolderButton = false;
            var result = fbd.ShowDialog();
            rootFolder = fbd.SelectedPath;
            if (result != DialogResult.OK || string.IsNullOrWhiteSpace(rootFolder))
            {
                paths = null;
                return false;
            }
        }

        paths = DirSearch(rootFolder, includeSubfolders, ImageExtensions);

        return true;
    }

    public string ShowFolderDialog()
    {
        using var fbd = new FolderBrowserDialog();
        fbd.ShowNewFolderButton = false;
        var result = fbd.ShowDialog();
        if (result != DialogResult.OK || string.IsNullOrWhiteSpace(fbd.SelectedPath))
        {
            return null;
        }
        return fbd.SelectedPath;
    }

    public List<string> ScanImageFiles(string rootFolder, bool includeSubfolders, IProgress<int> progress = null, CancellationToken ct = default)
    {
        var list = new List<string>();
        DirSearchWithProgress(rootFolder, includeSubfolders, ImageExtensions, list, progress, ct);
        return list;
    }

    public List<FileInfo> ScanImageFileInfos(string rootFolder, bool includeSubfolders, IProgress<int> progress = null, CancellationToken ct = default)
    {
        var list = new List<FileInfo>();
        DirSearchFileInfos(rootFolder, includeSubfolders, ImageExtensions, list, progress, ct);
        return list;
    }

    private static void DirSearchFileInfos(string sDir, bool includeSubfolders, string[] types, List<FileInfo> list, IProgress<int> progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var dirInfo = new DirectoryInfo(sDir);
            var files = dirInfo.EnumerateFiles()
                .Where(f => types.Any(o => o.Equals(f.Extension, StringComparison.InvariantCultureIgnoreCase)));

            list.AddRange(files);
            progress?.Report(list.Count);

            if (includeSubfolders)
            {
                foreach (var d in dirInfo.EnumerateDirectories())
                {
                    ct.ThrowIfCancellationRequested();
                    DirSearchFileInfos(d.FullName, true, types, list, progress, ct);
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    private static void DirSearchWithProgress(string sDir, bool includeSubfolders, string[] types, List<string> list, IProgress<int> progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var files = Directory.GetFiles(sDir)
                .Where(f => types.Any(o => o.Equals(Path.GetExtension(f), StringComparison.InvariantCultureIgnoreCase)));

            list.AddRange(files);
            progress?.Report(list.Count);

            if (includeSubfolders)
            {
                foreach (var d in Directory.GetDirectories(sDir))
                {
                    ct.ThrowIfCancellationRequested();
                    DirSearchWithProgress(d, true, types, list, progress, ct);
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    private static List<string> DirSearch(string sDir, bool includeSubfolders, params string[] types)
    {
        var list = new List<string>();
        try
        {
            list.AddRange(Directory.GetFiles(sDir)
                .Where(f => types.Any(o => o.Equals(Path.GetExtension(f), StringComparison.InvariantCultureIgnoreCase))));

            if (includeSubfolders)
            {
                foreach (var d in Directory.GetDirectories(sDir))
                {
                    list.AddRange(DirSearch(d, true, types));
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
        return list;
    }
}