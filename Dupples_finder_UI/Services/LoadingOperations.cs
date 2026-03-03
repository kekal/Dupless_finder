using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Dupples_finder_UI.Services.Interfaces;

namespace Dupples_finder_UI.Services;

public class LoadingOperations : ILoadingOperations
{
    public bool GetAllPaths(out IEnumerable<string> paths, string rootFolder)
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

        paths = DirSearch(rootFolder, ".jpg", ".png", ".jpeg", ".bmp", ".tiff", ".tif", ".webp");

        return true;
    }

    private static List<string> DirSearch(string sDir, params string[] types)
    {
        var list = new List<string>();
        try
        {
            list.AddRange(Directory.GetFiles(sDir)
                .Where(f => types.Any(o => o.Equals(Path.GetExtension(f), StringComparison.InvariantCultureIgnoreCase))));

            foreach (var d in Directory.GetDirectories(sDir))
            {
                list.AddRange(DirSearch(d, types));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
        return list;
    }
}