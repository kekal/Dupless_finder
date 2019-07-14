using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using System.Windows.Controls;
using Dupless_finder;
using OpenCvSharp;
using OpenCvSharp.XFeatures2D;
using static System.Math;
using Timer = System.Threading.Timer;

namespace Dupples_finder_UI
{
    public class MainViewModel : DependencyObject
    {
        public static MainViewModel Inst;
        public MainViewModel()
        {
            PrepareCommands();
            Inst = this;
            IsLoaded = true;
            StartMemoryAmountPublishing();
        }
        
        // ==================================================================================================================
        
        #region Dependency connections

        //public static readonly DependencyProperty FilesProperty = DependencyProperty.Register("Files", typeof(string[]), typeof(MainViewModel), new PropertyMetadata(default(string[])));
        public static readonly DependencyProperty IsHardModeProperty = DependencyProperty.Register("IsHardMode", typeof(bool), typeof(MainViewModel), new PropertyMetadata(default(bool)));
        public static readonly DependencyProperty DataCollectionProperty = DependencyProperty.Register("DataCollection", typeof(IList<ImageInfo>), typeof(MainViewModel), new PropertyMetadata(new List<ImageInfo>()));
        public static readonly DependencyProperty ImageProperty = DependencyProperty.Register("Image", typeof(object), typeof(MainViewModel), new PropertyMetadata(default(BitmapImage)));
        public static readonly DependencyProperty IsLoadedProperty = DependencyProperty.Register("IsLoaded", typeof(bool), typeof(MainViewModel), new PropertyMetadata(default(bool)));
        public static readonly DependencyProperty CalcProgressProperty = DependencyProperty.Register("CalcProgress", typeof(double), typeof(MainViewModel), new PropertyMetadata(default(double)));
        public static readonly DependencyProperty AllocMemProperty = DependencyProperty.Register("AllocMem", typeof(long), typeof(MainViewModel), new PropertyMetadata(default(long)));
        public static readonly DependencyProperty IsProgrVisibleProperty = DependencyProperty.Register("IsProgrVisible", typeof(Visibility), typeof(MainViewModel), new PropertyMetadata(Visibility.Collapsed));

        #endregion

        // ==================================================================================================================

        #region Events

        public event EventHandler<long> MemoryDisplayEvent;
        public virtual void OnMemoryDisplayEvent(long alloc)
        {
            MemoryDisplayEvent?.Invoke(this, alloc);
        } 

        #endregion

        // ==================================================================================================================

        #region Fields

        private Timer _t;

        private ConcurrentDictionary<string, MatOfFloat> _hashesDict;
        
        // ==================================================================================================================
        
        #region Injected

        //public string[] Files
        //{
        //    get { return (string[])GetValue(FilesProperty); }
        //    set { SetValue(FilesProperty, value); }
        //}

        public IList<ImageInfo> DataCollection
        {
            get => (IList<ImageInfo>)GetValue(DataCollectionProperty);
            set => SetValue(DataCollectionProperty, value);
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

        public RelayCommand ComputeCommand { get; private set; }

        public Visibility IsProgrVisible
        {
            get { return (Visibility) GetValue(IsProgrVisibleProperty); }
            set { SetValue(IsProgrVisibleProperty, value); }
        }

        private void PrepareCommands()
        {
            LoadCommad = new RelayCommand(() =>
            {
                using (var fbd = new FolderBrowserDialogEx())
                {
                    fbd.ShowNewFolderButton = false;
                    DialogResult result = fbd.ShowDialog();

                    if (result != DialogResult.OK || string.IsNullOrWhiteSpace(fbd.SelectedPath))
                    {
                        return;
                    }

                    DataCollection = DirSearch(fbd.SelectedPath, ".jpg", ".png").Select(path => new ImageInfo {FilePath = path}).ToList();
                    LoadCollectionToMemory(DataCollection);
                }
            });

            ComputeCommand = new RelayCommand(() =>
            {
                IsProgrVisible = Visibility.Visible;
                Thread.CurrentThread.Priority = ThreadPriority.Highest;
                
                var pathCollection = DataCollection.Select(ii => ii.FilePath);
                var mainCalcThread = new Thread(() =>
                {
                    Thread.CurrentThread.Priority = ThreadPriority.Lowest;
                    Calc(pathCollection);
                });

                mainCalcThread.Start();
            });
        }

        #endregion

        // ==================================================================================================================

        #region Methods

        private void StartMemoryAmountPublishing()
        {
            _t = new Timer(_ => Dispatcher.BeginInvoke(new Func<long>(() =>
            {
                return AllocMem = Process.GetCurrentProcess().PrivateMemorySize64 / 1000000;
            })), null, 0, 300);
        }

        private void LoadCollectionToMemory(IList<ImageInfo> collection)
        {
            Inst.Dispatcher.Invoke(() => IsLoaded = false);

            Task.Factory.StartNew(() =>
            {
                foreach (var info in collection)
                {
                    var a = info.Image;
                }
            }).ContinueWith(e =>
            {
                Inst.Dispatcher.Invoke(MainWindow.Update);
                Inst.Dispatcher.Invoke(() => IsLoaded = true);
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

        public static ImageInfo GetImageInfo(Image image)
        {
            return Inst.DataCollection.FirstOrDefault(x => x.FilePath == (string)image.Tag);
        }

        public static IEnumerable<Program.Result> CreateMatchCollection(IDictionary<string, MatOfFloat> hashes)
        {
            var matchList = new ConcurrentBag<Program.Result>();

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
                    Interlocked.Increment(ref completedIterations);
                    var i1 = i;
                    var j1 = j;
                    var task = new Task(() =>
                    {
                        //Console.WriteLine("Calculate matchpoint for " + hashArray[i1].Key);
                        if (i1 % (hashArray.Length / 10 + 1) == 0)
                        {
                            Console.WriteLine("Calculate matchpoint for " + i1 + " and " + j1 + " of " + hashArray.Length);
                        }
                        var linearFactors = CalcLinearFactors(hashArray, j1, i1);
                        matchList.Add(new Program.Result(hashArray[j1].Key, hashArray[i1].Key, linearFactors.Item1));
                    });

                    tasks.Add(task);

                    task.Start();
                }
            }

            Task.WaitAll(tasks.ToArray());
            return matchList.OrderBy(o => Abs(o.Match));
        }

        private static Tuple<double, double> CalcLinearFactors(KeyValuePair<string, MatOfFloat>[] hashArray, int j, int i)
        {
            //Console.WriteLine("Calculate matchpoint for " + hashArray[j].Key + " and " + hashArray[i].Key);

            var bfMatches = new BFMatcher(NormTypes.L2, crossCheck: true).Match(hashArray[j].Value, hashArray[i].Value).OrderBy(o => o.Distance).Select(o => o.Distance);
            var matches = bfMatches.Take(bfMatches.Count() / 2).ToArray();

            var xes = new List<double>();
            var yes = new List<double>();

            for (int k = 0; k < matches.Length; k++)
            {
                xes.Add(k);
                yes.Add(matches[k]);
            }
            var linearFactors = MathNet.Numerics.Fit.Line(xes.ToArray(), yes.ToArray());
            //Console.WriteLine("\t\t x * " + linearFactors.Item2 + " + " + linearFactors.Item1);
            return linearFactors;
        }

        void Calc(IEnumerable<string> pathes)
        {
            //var pathes = DataCollection.Select(ii => ii.FilePath);
            var thumbSize = 100;

            var currentProgress = 0.0;
            double minProgressStep = 100.0 / pathes.Count();
            var updateBarStep = 0;

            _hashesDict = new ConcurrentDictionary<string, MatOfFloat>();
            var tasks = new List<Task>();

            foreach (var path in pathes)
            {
                var task = new Task(() =>
                {
                    Thread.CurrentThread.Priority = ThreadPriority.Lowest;

                    var sourceMat = new Mat(path);

                    var scale = (double)thumbSize / Max(sourceMat.Width, sourceMat.Height);
                    var resizedMat = sourceMat.Resize(new OpenCvSharp.Size(0, 0), scale, scale, InterpolationFlags.Nearest);

                    var grayScaledMat = new Mat();
                    Cv2.CvtColor(resizedMat, grayScaledMat, ColorConversionCodes.BGR2GRAY);


                    var siftPoints = SIFT.Create();

                    var descriptors = new MatOfFloat();

                    //Console.WriteLine("Creating hash for " + path);
                    //var keypoints = sift.Detect(gray).Take(KEYPOINTS_NUMBER).ToArray();
                    //sift.Compute(gray, ref keypoints, descriptors);
                    siftPoints.DetectAndCompute(grayScaledMat, null, out KeyPoint[] keypoints, descriptors);

                    _hashesDict.TryAdd(path, descriptors);

                    resizedMat?.Dispose();
                    siftPoints.Dispose();
                    grayScaledMat.Dispose();
                    sourceMat.Dispose();

                    currentProgress += minProgressStep;
                    updateBarStep++;
                    if (updateBarStep % 10 == 0)
                    {
                        updateBarStep = 0;
                        Inst.Dispatcher.BeginInvoke(new Func<double>(() =>
                        {
                            if (currentProgress >= 99.5)
                            {
                                IsProgrVisible = Visibility.Collapsed;
                            }
                            return CalcProgress = currentProgress;
                        }));
                    }
                });

                tasks.Add(task);
            }
            foreach (var task in tasks)
            {
                task.Start();
            }
            //Task.WaitAll(tasks.ToArray());
        }

        #endregion

        // ==================================================================================================================
    }

    public class ImageInfo
    {
        public ImageInfo()
        {
            PrepareCommands();
        }

        private void PrepareCommands()
        {
            ImageClick = new RelayCommand(OpenImageRoutine);
        }

        private byte[] _buffer;
        private BitmapImage _image;
        public BitmapImage Image
        {
            get
            {
                if (_image != null)
                {
                    return _image;
                }

                var DecodeSize = 200;
                // Only load thumbnails
                _buffer = null;
                _buffer = File.ReadAllBytes(FilePath);
                var mem = new MemoryStream(_buffer);
                var reducedImage = new BitmapImage();
                reducedImage.BeginInit();
                reducedImage.DecodePixelHeight = DecodeSize;
                reducedImage.CacheOption = BitmapCacheOption.OnDemand;
                reducedImage.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                //_reducedImage.DecodePixelWidth = DecodeSize;

                reducedImage.Rotation = Rotation.Rotate0;
                reducedImage.StreamSource = mem;
                //reducedImage.StreamSource = new FileStream(FilePath,FileMode.Open);
                reducedImage.EndInit();
                //buffer = null;
                reducedImage.Freeze();

                //if (currentProc.PrivateMemorySize64 / 1048576 < 200)
                //{
                    _image = reducedImage;
                //}

                return reducedImage;
            }
            set
            {
                if (!ReferenceEquals(_image, value))
                {
                    _image = value;
                }
            }
        }

        public string FilePath { get; set; }

        public RelayCommand ImageClick { get; private set; }

        private void OpenImageRoutine()
        {
            Process.Start(FilePath);
        }
    }
}
