using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Media;
using Dupless_finder;
using OpenCvSharp;
using OpenCvSharp.XFeatures2D;
using static System.Math;
using Image = System.Windows.Controls.Image;
using Timer = System.Threading.Timer;
using System.Runtime.Remoting;
using System.Windows.Markup;

namespace Dupples_finder_UI
{
    public class MainViewModel : DependencyObject, IDisposable
    {
        public static MainViewModel Inst;
        public MainViewModel()
        {
            Inst = this;
            PrepareCommands();
            ThumbnailSize = 200;
            IsLoaded = true;
            PairDataCollection = new List<ImagePair>();
            DataCollection = new List<ImageInfo>();
            StartMemoryAmountPublishing();
        }
        
        // ==================================================================================================================
        
        #region Dependency connections

        //public static readonly DependencyProperty FilesProperty = DependencyProperty.Register("Files", typeof(string[]), typeof(MainViewModel), new PropertyMetadata(default(string[])));
        public static readonly DependencyProperty IsHardModeProperty = DependencyProperty.Register("IsHardMode", typeof(bool), typeof(MainViewModel), new PropertyMetadata(default(bool)));
        public static readonly DependencyProperty DataCollectionProperty = DependencyProperty.Register("DataCollection", typeof(IList<ImageInfo>), typeof(MainViewModel), new PropertyMetadata(new List<ImageInfo>()));
        public static readonly DependencyProperty PairDataCollectionProperty = DependencyProperty.Register("PairDataCollection", typeof(IList<ImagePair>), typeof(MainViewModel), new PropertyMetadata(default(IList<ImagePair>)));
        public static readonly DependencyProperty ImageProperty = DependencyProperty.Register("Image", typeof(object), typeof(MainViewModel), new PropertyMetadata(default(BitmapImage)));
        public static readonly DependencyProperty IsLoadedProperty = DependencyProperty.Register("IsLoaded", typeof(bool), typeof(MainViewModel), new PropertyMetadata(default(bool)));
        public static readonly DependencyProperty CalcProgressProperty = DependencyProperty.Register("CalcProgress", typeof(double), typeof(MainViewModel), new PropertyMetadata(default(double)));
        public static readonly DependencyProperty AllocMemProperty = DependencyProperty.Register("AllocMem", typeof(long), typeof(MainViewModel), new PropertyMetadata(default(long)));
        public static readonly DependencyProperty IsProgrVisibleProperty = DependencyProperty.Register("IsProgrVisible", typeof(Visibility), typeof(MainViewModel), new PropertyMetadata(Visibility.Collapsed));

        #endregion

        // ==================================================================================================================

        #region Events

        //public event EventHandler<long> MemoryDisplayEvent;
        //public virtual void OnMemoryDisplayEvent(long alloc)
        //{
        //    MemoryDisplayEvent?.Invoke(this, alloc);
        //} 

        #endregion

        // ==================================================================================================================

        #region Fields


        private ConcurrentDictionary<string, MatOfFloat> _hashesDict;
        private IEnumerable<Program.Result> _matches;
        
        // ==================================================================================================================
        
        #region Injected

        public ushort ThumbnailSize { get; }

        public IList<ImageInfo> DataCollection
        {
            get => (IList<ImageInfo>)GetValue(DataCollectionProperty);
            set
            {
                //if (DataCollection != null)
                //    foreach (var img in DataCollection)
                //    {
                //        img?.Dispose();
                //    }
                SetValue(DataCollectionProperty, value);
            }
        }

        public IList<ImagePair> PairDataCollection
        {
            get => (IList<ImagePair>)GetValue(PairDataCollectionProperty);
            set
            {
                //if (PairDataCollection != null)
                //    foreach (var img in PairDataCollection)
                //    {
                //        img?.Dispose();
                //    }
                SetValue(PairDataCollectionProperty, value);
            }
        }

        public bool IsHardMode
        {
            get => (bool)GetValue(IsHardModeProperty);
            set => SetValue(IsHardModeProperty, value);
        }

        public bool IsLoaded
        {
            get { return (bool)GetValue(IsLoadedProperty); }
            set { SetValue(IsLoadedProperty, value); }
        }

        public Visibility IsProgrVisible
        {
            get { return (Visibility)GetValue(IsProgrVisibleProperty); }
            set { SetValue(IsProgrVisibleProperty, value); }
        }

        public double CalcProgress
        {
            get { return (double)GetValue(CalcProgressProperty); }
            set { SetValue(CalcProgressProperty, value); }
        }

        public long AllocMem
        {
            get { return (long)GetValue(AllocMemProperty); }
            set { SetValue(AllocMemProperty, value); }
        } 
        #endregion
        #endregion

        // ==================================================================================================================

        #region Commands

        public RelayCommand LoadCommad { get; private set; }
        public RelayCommand CreateHashes { get; private set; }
        public RelayCommand LoadTemplateCollection { get; private set; }
        public RelayCommand CreateHashesFromCollection { get; private set; }

        private void PrepareCommands()
        {
            LoadCommad = new RelayCommand(() =>
            {
                if (!GetAllPathes(out var pathes, string.Empty))
                {
                    return;
                }

                DataCollection = pathes.Select(path => new ImageInfo(path)).ToList();
                //LoadCollectionToMemory(DataCollection);
            });

            //CreateHashes = new RelayCommand(() =>
            //{
            //    IsProgrVisible = Visibility.Visible;
            //    Thread.CurrentThread.Priority = ThreadPriority.Highest;
            //    var pathCollection = DataCollection.Select(ii => ii.FilePath);

            //    CalcSiftHashes(pathCollection)
            //        .ContinueWith(e1 => { _matches = CreateMatchCollection(_hashesDict); })
            //        .ContinueWith(e2 => { PopulateDupes(); });
            //});

            LoadTemplateCollection = new RelayCommand(() =>
            {
                IList<ImagePair> colle = new List<ImagePair>();
                if (!GetAllPathes(out var pathes, ""/*@"C:\Users\Main\Desktop\cosplay\MasyuTaitu"*/))
                {
                    return;
                }

                if (DataCollection?.Count > 0)
                {
                    var oldPathes = DataCollection.Select(ii => ii.FilePath);
                    var absentPathes = pathes.Where(p => !oldPathes.Contains(p));
                    var tempCollection = absentPathes.Select(path => new ImageInfo(path)).ToList();
                    tempCollection.AddRange(DataCollection);
                    DataCollection = tempCollection;
                }
                else
                {
                    DataCollection = pathes.Select(path => new ImageInfo(path)).ToList();
                }

                
                for (var i = 1; i < DataCollection.Count; i += 2)
                {
                    colle.Add(new ImagePair
                    {
                        Image1 = DataCollection[i - 1],
                        Image2 = DataCollection[i]
                    });
                }
                PairDataCollection = colle;

                //LoadCollectionToMemory(DataCollection);
            });

            CreateHashesFromCollection = new RelayCommand(() =>
            {
                IsProgrVisible = Visibility.Visible;
                Thread.CurrentThread.Priority = ThreadPriority.Highest;
                //var pathCollection = DataCollection.Select(ii => ii.FilePath);

                CalcSiftHashes(DataCollection)
                    .ContinueWith(e1 => { _matches = CreateMatchCollection(_hashesDict); })
                    .ContinueWith(e2 => { PopulateDupes(); });
            });
        }


        #endregion

        // ==================================================================================================================

        #region Methods

        private static bool GetAllPathes(out IEnumerable<string> pathes, string rootFolder)
        {
            if (rootFolder == string.Empty)
            {
                using (var fbd = new FolderBrowserDialogEx())
                {
                    fbd.ShowNewFolderButton = false;
                    var result = fbd.ShowDialog();
                    rootFolder = fbd.SelectedPath;
                    if (result != DialogResult.OK || string.IsNullOrWhiteSpace(rootFolder))
                    {
                        pathes = null;
                        return false;
                    }
                }
            }

            pathes = DirSearch(rootFolder, ".jpg", ".png");

            return true;
        }

        private Timer _t;
        private void StartMemoryAmountPublishing()
        {
            _t = new Timer(_ => Dispatcher.BeginInvoke(new Func<long>(() =>
            {
                return AllocMem = Process.GetCurrentProcess().PrivateMemorySize64 / 1000000;
            })), null, 0, 300);
        }

        private void PopulateDupes()
        {
            Dispatcher.Invoke(() =>
            {
                var temp = _matches.Select(match => new ImagePair
                {
                    Image1 = DataCollection.FirstOrDefault(im => im.FilePath == match.Name1),
                    Image2 = DataCollection.FirstOrDefault(im => im.FilePath == match.Name2),
                    Match = match.Match
                }).ToList();

                LoadCollectionDupesToMemory(temp);
                PairDataCollection = temp;
            });
        }

        private void LoadCollectionToMemory(IList<ImageInfo> collection)
        {
            Dispatcher.Invoke(() => IsLoaded = false);
            IsProgrVisible = Visibility.Visible;
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
                Parallel.ForEach(collection, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount - 1 }, info =>
                {
                    Thread.CurrentThread.Priority = ThreadPriority.Lowest;
                    Thread.Sleep(1);
                    var a = info.Image;
                    Dispatcher.BeginInvoke(new Func<bool>(() =>
                    {
                        currentProgress += minProgressStep;
                        CalcProgress = currentProgress;
                        if (Abs(currentProgress - 100) < 0.1)
                        {
                            IsProgrVisible = Visibility.Collapsed;
                        }
                        return false;
                    }));
                });
            }).ContinueWith(e =>
            {
                Dispatcher.Invoke(MainWindow.Update);
                Dispatcher.Invoke(() => IsLoaded = true);
            });
        }

        private void LoadCollectionDupesToMemory(IList<ImagePair> collection)
        {
            Dispatcher.Invoke(() => IsLoaded = false);

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
                Dispatcher.Invoke(() => IsLoaded = true);
            });
        }

        static IEnumerable<string> DirSearch(string sDir, params string[] types)
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

        //public ImageInfo GetImageInfo(Image image)
        //{
        //    return DataCollection.FirstOrDefault(x => x.FilePath == (string)image.Tag);
        //}
        
        #region SIFT Calculations

        public IEnumerable<Program.Result> CreateMatchCollection(IDictionary<string, MatOfFloat> hashes)
        {
            Dispatcher.BeginInvoke(new Func<bool>(() => { IsProgrVisible = Visibility.Visible; return false; }));

            var matchList = new ConcurrentBag<Program.Result>();

            var currentProgress = 0.0;
            var minProgressStep = 100.0 / (hashes.Count /2 * (hashes.Count - 1));
            var tasks = new List<Task>();
            int completedIterations = 0;
            var hashArray = hashes.ToArray();
            for (var j = 0; j < hashArray.Length; j++)
            {
                for (var i = j + 1; i < hashArray.Length; i++)
                {
                    if (hashArray[j].Key == hashArray[i].Key)
                    {
                        continue;
                    }
                    //Thread.Sleep(1);
                    Interlocked.Increment(ref completedIterations);
                    var i1 = i;
                    var j1 = j;
                    var task = new Task(() =>
                    {
                        Thread.CurrentThread.Priority = ThreadPriority.Lowest;
                        //if (i1 % (hashArray.Length / 10 + 1) == 0)
                        //{
                        //    Console.WriteLine(@"Calculate matchpoint for " + i1 + @" and " + j1 + @" of " + hashArray.Length);
                        //}
                        var linearFactors = CalcLinearFactors(hashArray, j1, i1);
                        matchList.Add(new Program.Result(hashArray[j1].Key, hashArray[i1].Key, linearFactors.Item1));

                        Dispatcher.BeginInvoke(new Func<bool>(() =>
                        {
                            currentProgress += minProgressStep;
                            CalcProgress = currentProgress;
                            if (Abs(currentProgress - 100) < 0.1)
                            {
                                IsProgrVisible = Visibility.Collapsed;
                            }
                            return false;
                        }));
                    });

                    tasks.Add(task);
                }
            }
            foreach (var t in tasks)       {
                t.Start();
            }

            Task.WaitAll(tasks.ToArray());
            return matchList.OrderBy(o => Abs(o.Match));
        }

        private static Tuple<double, double> CalcLinearFactors(KeyValuePair<string, MatOfFloat>[] hashArray, int j, int i)
        {
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;
            var matcher = new BFMatcher(NormTypes.L2SQR, crossCheck: true);
            var bfMatches = matcher.Match(hashArray[j].Value, hashArray[i].Value).OrderBy(o => o.Distance).Select(o => o.Distance).ToList();
            matcher.Dispose();

            float[] matches;
            if (bfMatches.Count > 10)
            {
                matches = bfMatches.Take(bfMatches.Count / 2).ToArray();
            }
            else if (bfMatches.Count < 2)
            {
                matches = new[] {float.MaxValue, float.MaxValue};
            }
            else
            {
                matches = bfMatches.Take(bfMatches.Count).ToArray();
            }

            var xes = new List<double>();
            var yes = new List<double>();

            for (int k = 0; k < matches.Length; k++)
            {
                xes.Add(k);
                yes.Add(matches[k]);
            }
            
            var linearFactors = MathNet.Numerics.Fit.Line(xes.ToArray(), yes.ToArray());
            return linearFactors;
        }

        //private Task CalcSiftHashes(IEnumerable<string> paths, int thumbSize = 100)
        //{
        //    var currentProgress = 0.0;
        //    var minProgressStep = 100.0 / paths.Count();

        //    if (_hashesDict?.Values != null)
        //    {
        //        foreach (var mat in _hashesDict.Values)
        //        {
        //            mat?.Dispose();
        //        }
        //    }

        //    _hashesDict = new ConcurrentDictionary<string, MatOfFloat>();

        //    var tasks = new List<Task>();
        //    foreach (var path in paths)
        //    {
        //        var task = new Task(() =>
        //        {
        //            Thread.CurrentThread.Priority = ThreadPriority.Lowest;
        //            Thread.Sleep(1000);
        //            Mat sourceMat = new Mat(path, ImreadModes.GrayScale);
        //            var resizedMat = sourceMat.Resize(new OpenCvSharp.Size(thumbSize, thumbSize), 0, 0, InterpolationFlags.Nearest);

        //            //var scale = (double)thumbSize / Max(sourceMat.Width, sourceMat.Height);
        //            //var resizedMat = sourceMat.Resize(new OpenCvSharp.Size(0, 0), scale, scale, InterpolationFlags.Nearest);

        //            //var grayScaledMat = new Mat();
        //            //Cv2.CvtColor(resizedMat, grayScaledMat, ColorConversionCodes.BGR2GRAY);

        //            SIFT siftPoints = SIFT.Create();

        //            var descriptors = new MatOfFloat();

        //            //var keypoints = sift.Detect(gray).Take(KEYPOINTS_NUMBER).ToArray();
        //            //sift.Compute(gray, ref keypoints, descriptors);
        //            siftPoints.DetectAndCompute(resizedMat, null, out KeyPoint[] keypoints, descriptors);

        //            _hashesDict.TryAdd(path, descriptors);

        //            //resizedMat?.Dispose();
        //            siftPoints.Dispose();
        //            //grayScaledMat.Dispose();
        //            resizedMat.Release();
        //            sourceMat.Release();

        //            currentProgress += minProgressStep;
        //            Dispatcher.BeginInvoke(new Func<bool>(() =>
        //            {
        //                CalcProgress = currentProgress;
        //                if (Abs(currentProgress - 100) < 0.1)
        //                {
        //                    IsProgrVisible = Visibility.Collapsed;
        //                }
        //                return false;
        //            }));
        //        });

        //        tasks.Add(task);
        //    }
        //    foreach (var task in tasks)
        //    {
        //        task.Start();
        //    }
        //    return Task.WhenAll(tasks.ToArray());
        //}

        private Task CalcSiftHashes(IEnumerable<ImageInfo> infos, int thumbSize = 100)
        {
            Trace.WriteLine($"CalcSiftHashes started");
            var currentProgress = 0.0;
            var minProgressStep = 100.0 / infos.Count();

            if (_hashesDict?.Values != null)
            {
                foreach (var mat in _hashesDict.Values)
                {
                    mat?.Release();
                }
            }

            _hashesDict = new ConcurrentDictionary<string, MatOfFloat>();

            var tasks = new List<Task>();
            foreach (ImageInfo info in infos)
            {
                var task = new Task(() =>
                {
                    Thread.CurrentThread.Priority = ThreadPriority.Lowest;

                    Thread.Sleep(1);
                    
                    var mem = new MemoryStream();

                    // copy to byte array
                    int stride = info.Image.PixelWidth * 4;
                    byte[] buffer = new byte[stride * info.Image.PixelHeight];
                    info.Image.CopyPixels(buffer, stride, 0);
                    
                    // create bitmap
                    Bitmap bitmap = new Bitmap(info.Image.PixelWidth, info.Image.PixelHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                    // lock bitmap data
                    BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, bitmap.PixelFormat);

                    // copy byte array to bitmap data
                    Marshal.Copy(buffer, 0, bitmapData.Scan0, buffer.Length);

                    // unlock
                    bitmap.UnlockBits(bitmapData);

                    bitmap.Save(mem,ImageFormat.Bmp);


                    Mat sourceMat = Cv2.ImDecode(mem.GetBuffer(), ImreadModes.Unchanged);
                    var resizedMat = sourceMat.Resize(new OpenCvSharp.Size(thumbSize, thumbSize), 0, 0, InterpolationFlags.Nearest);

                    //var scale = (double)thumbSize / Max(sourceMat.Width, sourceMat.Height);
                    //var resizedMat = sourceMat.Resize(new OpenCvSharp.Size(0, 0), scale, scale, InterpolationFlags.Nearest);

                    //var grayScaledMat = new Mat();
                    //Cv2.CvtColor(resizedMat, grayScaledMat, ColorConversionCodes.BGR2GRAY);

                    var siftPoints = SIFT.Create();

                    var descriptors = new MatOfFloat();

                    //var keypoints = sift.Detect(gray).Take(KEYPOINTS_NUMBER).ToArray();
                    //sift.Compute(gray, ref keypoints, descriptors);
                    siftPoints.DetectAndCompute(resizedMat, null, out KeyPoint[] keypoints, descriptors);

                    _hashesDict.TryAdd(info.FilePath, descriptors);

                    //resizedMat?.Dispose();
                    siftPoints.Dispose();
                    //grayScaledMat.Dispose();
                    resizedMat.Release();
                    sourceMat.Release();

                    currentProgress += minProgressStep;
                    Dispatcher.BeginInvoke(new Func<bool>(() =>
                    {
                        CalcProgress = currentProgress;
                        if (Abs(currentProgress - 100) < 0.1)
                        {
                            IsProgrVisible = Visibility.Collapsed;
                        }
                        return false;
                    }));
                });

                tasks.Add(task);
            }
            foreach (var task in tasks)
            {
                task.Start();
            }
            return Task.WhenAll(tasks.ToArray());
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _t.Dispose();
                _t = null;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
        #endregion


    }
}
