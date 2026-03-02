using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Dupples_finder_UI.Services.Interfaces;

namespace Dupples_finder_UI.Services
{
    public class LoadingOperations : ILoadingOperations
    {
        /// <summary>
        /// Opens a folder browser dialog and recursively scans for image files.
        /// </summary>
        public bool GetAllPaths(out IEnumerable<string> paths, string rootFolder)
        {
            if (rootFolder == string.Empty)
            {
                using var fbd = new FolderBrowserDialog();
                fbd.ShowNewFolderButton = false;
                var result = fbd.ShowDialog();
                rootFolder = fbd.SelectedPath;
                if (result != DialogResult.OK || String.IsNullOrWhiteSpace(rootFolder))
                {
                    paths = null;
                    return false;
                }
            }

            paths = DirSearch(rootFolder, ".jpg", ".png", ".jpeg", ".bmp", ".tiff", ".tif", ".webp");

            return true;
        }

        private static IEnumerable<string> DirSearch(string sDir, params string[] types)
        {
            var list = new List<string>();
            try
            {
                foreach (string f in Directory.GetFiles(sDir))
                {
                    if (types.Any(o => o.Equals(Path.GetExtension(f), StringComparison.InvariantCultureIgnoreCase)))
                    {
                        list.Add(f);
                    }
                }
                foreach (string d in Directory.GetDirectories(sDir))
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
}
