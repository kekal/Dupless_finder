using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Windows.Media.Imaging;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using OpenCvSharp;
using Timer = System.Threading.Timer;

namespace Dupples_finder_UI
{



    public sealed class MainViewModel : DependencyObject, IDisposable
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
            PreviewSize = GridWidth;
        }
        
        // ==================================================================================================================
        
        #region Dependency connections

        //public static readonly DependencyProperty FilesProperty = DependencyProperty.Register("Files", typeof(string[]), typeof(MainViewModel), new PropertyMetadata(default(string[])));
        //public static readonly DependencyProperty IsHardModeProperty = DependencyProperty.Register("IsHardMode", typeof(bool), typeof(MainViewModel), new PropertyMetadata(default(bool)));
        public static readonly DependencyProperty DataCollectionProperty = DependencyProperty.Register("DataCollection", typeof(IList<ImageInfo>), typeof(MainViewModel), new PropertyMetadata(new List<ImageInfo>()));
        public static readonly DependencyProperty PairDataCollectionProperty = DependencyProperty.Register("PairDataCollection", typeof(IList<ImagePair>), typeof(MainViewModel), new PropertyMetadata(default(IList<ImagePair>)));
        //public static readonly DependencyProperty ImageProperty = DependencyProperty.Register("Image", typeof(object), typeof(MainViewModel), new PropertyMetadata(default(BitmapImage)));
        public static readonly DependencyProperty IsLoadedProperty = DependencyProperty.Register("IsLoaded", typeof(bool), typeof(MainViewModel), new PropertyMetadata(default(bool)));
        public static readonly DependencyProperty CalcProgressProperty = DependencyProperty.Register("CalcProgress", typeof(double), typeof(MainViewModel), new PropertyMetadata(default(double)));
        public static readonly DependencyProperty CalcProgressTextProperty = DependencyProperty.Register("CalcProgressText", typeof(string), typeof(MainViewModel), new PropertyMetadata(default(string)));
        public static readonly DependencyProperty AllocMemProperty = DependencyProperty.Register("AllocMem", typeof(long), typeof(MainViewModel), new PropertyMetadata(default(long)));
        public static readonly DependencyProperty IsProgrVisibleProperty = DependencyProperty.Register("IsProgrVisible", typeof(Visibility), typeof(MainViewModel), new PropertyMetadata(Visibility.Collapsed));
        public static readonly DependencyProperty CurrentImageViewProperty = DependencyProperty.Register("CurrentImageView", typeof(ImageSource), typeof(MainViewModel), new PropertyMetadata(default(ImageSource)));
        public static readonly DependencyProperty PreviewSizeProperty = DependencyProperty.Register("PreviewSize", typeof(double), typeof(MainViewModel), new PropertyMetadata(default(double)));

        #endregion

        // ==================================================================================================================

        #region Fields

        //internal ConcurrentDictionary<string, MatOfFloat> _hashesDict;
        private IEnumerable<PairSimilarityInfo> _matches;

        // ==================================================================================================================

        private readonly CalcOperations _calcOperations;

        // ==================================================================================================================




        #region Injected properties

        public ushort ThumbnailSize { get; }

        /// <summary>Thumbnail list width computed from ThumbnailSize  </summary>
        public double GridWidth => 2 * ThumbnailSize + 80;

        private IList<ImageInfo> DataCollection
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

        //public bool IsHardMode
        //{
        //    get => (bool)GetValue(IsHardModeProperty);
        //    set => SetValue(IsHardModeProperty, value);
        //}

        public bool IsLoaded
        {
            get => (bool)GetValue(IsLoadedProperty);
            set => SetValue(IsLoadedProperty, value);
        }

        public Visibility IsProgrVisible
        {
            get => (Visibility)GetValue(IsProgrVisibleProperty);
            set => SetValue(IsProgrVisibleProperty, value);
        }

        public double CalcProgress
        {
            get => (double)GetValue(CalcProgressProperty);
            set
            {
                CalcProgressText = value.ToString("F1") + '%';
                SetValue(CalcProgressProperty, value);
            }
        }

        public string CalcProgressText
        {
            get => (string)GetValue(CalcProgressTextProperty);
            set => SetValue(CalcProgressTextProperty, value);
        }

        public long AllocMem
        {
            get => (long)GetValue(AllocMemProperty);
            set => SetValue(AllocMemProperty, value);
        }

        public ImageSource CurrentImageView
        {
            get => (ImageSource)GetValue(CurrentImageViewProperty);
            set => SetValue(CurrentImageViewProperty, value);
        }

        public double PreviewSize
        {
            get => (double) GetValue(PreviewSizeProperty);
            set => SetValue(PreviewSizeProperty, value);
        }

        #endregion
        #endregion

        // ==================================================================================================================

        #region Commands

        public RelayCommand LoadCommad { get; private set; }
        //public RelayCommand CreateHashes { get; private set; }
        public RelayCommand LoadTemplateCollection { get; private set; }
        public RelayCommand CreateHashesFromCollection { get; private set; }
        public RelayCommand CloseView { get; private set; }
        public RelayCommand<double> ZoomIn { get; private set; }
        public RelayCommand<double> ZoomOut { get; private set; }




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


                CreateDB(pathes.FirstOrDefault());
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
                    colle.Add(new ImagePair(ThumbnailSize)
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
                //IsProgrVisible = Visibility.Visible;
                Thread.CurrentThread.Priority = ThreadPriority.Highest;

                //ClearCollectionCache();
                var hashesDict = _calcOperations.CalcSiftHashes(DataCollection, out Task result);
                result.ContinueWith(e1 => _matches = _calcOperations.CreateMatchCollection(hashesDict).Distinct())
                      .ContinueWith(e2 => { PopulateDupes(); })
                      .ContinueWith(e3 =>
                      {
                          GC.Collect(0, GCCollectionMode.Forced);
                          GC.Collect(1, GCCollectionMode.Forced);
                          GC.Collect(2, GCCollectionMode.Forced);
                      });
            });

            CloseView = new RelayCommand(()=>
            {
                CurrentImageView = null;
                PreviewSize = GridWidth;
            });

            ZoomIn = new RelayCommand<double>(p =>
            {
                PreviewSize = p * 1.1 + 20;
            });

            ZoomOut = new RelayCommand<double>(p =>
            {
                PreviewSize = p * 0.9 + 20;
            });
        }

        private void CreateDB(string filePath)
        {
            if (File.Exists("test.db3"))
            {
                File.Delete("test.db3");
            }
            using (var connection = new SQLiteConnection("Data Source=test.db3;Version=3"))
            using (var command = new SQLiteCommand("CREATE TABLE PHOTOS(ID INTEGER PRIMARY KEY AUTOINCREMENT, PHOTO BLOB)", connection))
            {
                connection.Open();
                command.ExecuteNonQuery();

                var sourceMat = new Mat(filePath);
                var asdg = sourceMat.ToBytes();
                
             


                command.CommandText = "INSERT INTO PHOTOS (PHOTO) VALUES (@photo)";
                command.Parameters.Add("@photo", DbType.Binary, 100000).Value = asdg;
                command.ExecuteNonQuery();

                command.CommandText = "SELECT PHOTO FROM PHOTOS WHERE ID = 1";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        byte[] buffer = GetBytes(reader);
                    }
                }

            }
        }

        static byte[] GetBytes(SQLiteDataReader reader)
        {
            const int CHUNK_SIZE = 2 * 1024;
            byte[] buffer = new byte[CHUNK_SIZE];
            long bytesRead;
            long fieldOffset = 0;
            using (MemoryStream stream = new MemoryStream())
            {
                while ((bytesRead = reader.GetBytes(0, fieldOffset, buffer, 0, buffer.Length)) > 0)
                {
                    stream.Write(buffer, 0, (int)bytesRead);
                    fieldOffset += bytesRead;
                }
                return stream.ToArray();
            }
        }

        //private void ClearCollectionCache()
        //{
        //    if (_hashesDict != null)
        //    {
        //        foreach (var mat in _hashesDict.Values)
        //        {
        //            mat?.Release();
        //        }
        //    }
        //}

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
                var temp = _matches?.Take(DataCollection.Count).Select(match => new ImagePair(ThumbnailSize)
                {
                    Image1 = DataCollection.FirstOrDefault(im => im.FilePath == match.Hash1.Key),
                    Image2 = DataCollection.FirstOrDefault(im => im.FilePath == match.Hash2.Key),
                    Match = match.Match,
                }).ToList();

                LoadingOperations.LoadCollectionDupesToMemory(Inst, temp);
                PairDataCollection = temp;
            });
        }

        private void Dispose(bool disposing)
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
            //GC.SuppressFinalize(this);
        }

        #endregion

        public void Openview(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return;
                }

                var previewImage = new BitmapImage();
                var sourceMat = new Mat(filePath);
                previewImage.BeginInit();
                previewImage.StreamSource = sourceMat.ToMemoryStream(".jpg");
                previewImage.EndInit();
                previewImage.Freeze();
                sourceMat.Release();

                CurrentImageView = previewImage;
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
            }
        }
    }
}
