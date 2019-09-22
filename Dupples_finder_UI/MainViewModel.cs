using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Linq;
using System.Windows.Media.Imaging;
using System.Threading;
using OpenCvSharp;
using Timer = System.Threading.Timer;

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

            _calcOperations = new CalcOperations(Inst);
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

        #region Fields

        internal ConcurrentDictionary<string, MatOfFloat> _hashesDict;
        private IEnumerable<Result> _matches;

        // ==================================================================================================================

        private readonly CalcOperations _calcOperations;

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
            set { SetValue(IsProgrVisibleProperty, value);}
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
                if (!LoadingOperations.GetAllPathes(out var pathes, string.Empty))
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
                if (!LoadingOperations.GetAllPathes(out var pathes, ""/*@"C:\Users\Main\Desktop\cosplay\MasyuTaitu"*/))
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

                LoadingOperations.LoadCollectionToMemory(Inst, DataCollection);
            });

            CreateHashesFromCollection = new RelayCommand(() =>
            {
                IsProgrVisible = Visibility.Visible;
                Thread.CurrentThread.Priority = ThreadPriority.Highest;

                CalcOperations.CalcSiftHashes(Inst, DataCollection)
                    .ContinueWith(e1 => { _matches = _calcOperations.CreateMatchCollection(_hashesDict); })
                    .ContinueWith(e2 => { PopulateDupes(); });
            });
        }


        #endregion

        // ==================================================================================================================

        #region Methods

        private Timer _t;
        

        private void StartMemoryAmountPublishing()
        {
            _t = new Timer(_ => Dispatcher?.BeginInvoke(new Func<long>(() =>
            {
                return AllocMem = Process.GetCurrentProcess().PrivateMemorySize64 / 1000000;
            })), null, 0, 300);
        }

        //private Timer _tProg;
        //private double Progress = 0;
        //private void StartUpdateProgress()
        //{
        //    _tProg = new Timer(_ => Dispatcher?.BeginInvoke(new Func<double>(() =>
        //    {
        //        return CalcProgress = Progress;
        //    })), null, 0, 300);
        //}

        private void PopulateDupes()
        {
            Dispatcher?.Invoke(() =>
            {
                var temp = _matches.Select(match => new ImagePair
                {
                    Image1 = DataCollection.FirstOrDefault(im => im.FilePath == match.Name1),
                    Image2 = DataCollection.FirstOrDefault(im => im.FilePath == match.Name2),
                    Match = match.Match,
                    MatchPoints = match.MatchPoints
                }).ToList();

                LoadingOperations.LoadCollectionDupesToMemory(Inst, temp);
                PairDataCollection = temp;
            });
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _t.Dispose();
                _t = null;
                //_tProg.Dispose();
                //_tProg = null;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
