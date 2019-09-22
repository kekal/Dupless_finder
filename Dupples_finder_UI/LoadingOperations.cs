using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace Dupples_finder_UI
{
    internal static class LoadingOperations
    {
        public static bool GetAllPathes(out IEnumerable<string> pathes, string rootFolder)
        {
            if (rootFolder == string.Empty)
            {
                using (var fbd = new FolderBrowserDialogEx())
                {
                    fbd.ShowNewFolderButton = false;
                    var result = fbd.ShowDialog();
                    rootFolder = fbd.SelectedPath;
                    if (result != DialogResult.OK || String.IsNullOrWhiteSpace(rootFolder))
                    {
                        pathes = null;
                        return false;
                    }
                }
            }

            pathes = DirSearch(rootFolder, ".jpg", ".png");

            return true;
        }

        public static void LoadCollectionToMemory(MainViewModel mainViewModel, IList<ImageInfo> collection)
        {
            mainViewModel.Dispatcher?.Invoke(() => mainViewModel.IsLoaded = false);
            mainViewModel.IsProgrVisible = Visibility.Visible;
            Task.Factory.StartNew(() =>
            {
                Thread.CurrentThread.Priority = ThreadPriority.Lowest;
                double currentProgress = 0.0;
                double minProgressStep = 100.0 / collection.Count;

                //foreach (var info in collection)
                //{
                //    Thread.Sleep(1);
                //    var a = info.Image;
                //    Dispatcher.BeginInvoke(new Func<bool>(() =>
                //    {
                //        currentProgress += minProgressStep;
                //        CalcProgress = currentProgress;
                //        if (Abs(currentProgress - 100) < 0.1)
                //        {
                //            IsProgrVisible = Visibility.Collapsed;
                //        }
                //        return false;
                //    }));
                //}
                Parallel.ForEach(collection,
                    new ParallelOptions {MaxDegreeOfParallelism = Environment.ProcessorCount - 1}, info =>
                    {
                        Thread.CurrentThread.Priority = ThreadPriority.Lowest;
                        Thread.Sleep(1);
                        var a = info.Image;
                        mainViewModel.Dispatcher?.BeginInvoke(new Func<bool>(() =>
                        {
                            currentProgress += minProgressStep;
                            mainViewModel.CalcProgress = currentProgress;
                            if (Math.Abs(currentProgress - 100) < 0.1)
                            {
                                mainViewModel.IsProgrVisible = Visibility.Collapsed;
                            }
                            return false;
                        }));
                    });
            }).ContinueWith(e =>
            {
                mainViewModel.Dispatcher?.Invoke(MainWindow.Update);
                mainViewModel.Dispatcher?.Invoke(() => mainViewModel.IsLoaded = true);
            });
        }

        public static void LoadCollectionDupesToMemory(MainViewModel mainViewModel, IList<ImagePair> collection)
        {
            mainViewModel.Dispatcher?.Invoke(() => mainViewModel.IsLoaded = false);

            Task.Factory.StartNew(() =>
            {
                foreach (var pair in collection)
                {
                    var a = pair.Image1;
                    var b = pair.Image2;
                }
            }).ContinueWith(e =>
            {
                //Dispatcher.Invoke(MainWindow.Update);
                mainViewModel.Dispatcher?.Invoke(() => mainViewModel.IsLoaded = true);
            });
        }

        private static IEnumerable<string> DirSearch(string sDir, params string[] types)
        {
            var list = new List<string>();
            try
            {
                foreach (string f in Directory.GetFiles(sDir))
                {
                    if (types.Any(o => o.Equals(Path.GetExtension(f))))
                    {
                        list.Add(f);
                    }
                }
                foreach (string d in Directory.GetDirectories(sDir))
                {
                    list.AddRange(DirSearch(d, types));
                }
            }
            catch (Exception excpt)
            {
                Console.WriteLine(excpt.Message);
            }
            return list;
        }
    }
}