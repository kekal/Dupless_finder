using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using System.Windows.Media;
using MessageBox = System.Windows.MessageBox;

namespace Dupples_finder_UI
{
    public class MainViewModel : DependencyObject
    {
        private static MainViewModel inst;

        public MainViewModel()
        {
            IsLoaded = true;
            PrepareCommands();
            inst = this;
        }

        //public static readonly DependencyProperty FilesProperty = DependencyProperty.Register("Files", typeof(string[]), typeof(MainViewModel), new PropertyMetadata(default(string[])));
        public static readonly DependencyProperty IsHardModeProperty = DependencyProperty.Register("IsHardMode", typeof(bool), typeof(MainViewModel), new PropertyMetadata(default(bool)));
        public static readonly DependencyProperty DataCollectionProperty = DependencyProperty.Register("DataCollection", typeof(IList<ImageInfo>), typeof(MainViewModel), new PropertyMetadata(new List<ImageInfo>()));
        
        public static readonly DependencyProperty ImageProperty = DependencyProperty.Register("Image", typeof(object), typeof(MainViewModel), new PropertyMetadata(default(BitmapImage)));
        public static readonly DependencyProperty IsLoadedProperty = DependencyProperty.Register("IsLoaded", typeof(bool), typeof(MainViewModel), new PropertyMetadata(default(bool)));

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

        #region Commands
        // ==================================================================================================================
        public RelayCommand SaveCommand { get; private set; }


        private void PrepareCommands()
        {
            SaveCommand = new RelayCommand(() =>
            {
                using (var fbd = new FolderBrowserDialog())
                {
                    DialogResult result = fbd.ShowDialog();

                    if (result != DialogResult.OK || string.IsNullOrWhiteSpace(fbd.SelectedPath))
                    {
                        return;
                    }

                    DataCollection = DirSearch(fbd.SelectedPath, ".jpg", ".png").Select(path => new ImageInfo {FilePath = path}).ToList();


                    LoadCollectionToMemory(DataCollection);

                }
            });
        }

        //void SetLoading()
        //{
        //    if (Table1)
        //    {
                
        //    }
        //}

        //void SetLoaded()
        //{
        //    throw new NotImplementedException();
        //}

        private void LoadCollectionToMemory(IList<ImageInfo> collection)
        {
            var qwer = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            var ins = inst;
            Task.Factory.StartNew(() =>
            {
                Thread.Sleep(3000);
                ins.Dispatcher.Invoke(() =>
                {
                    IsLoaded = false;
                });

                foreach (var info in collection)
                {
                    var a = info.Image;
                }
            }).ContinueWith(e =>
            {
                ins.Dispatcher.Invoke(() =>
                {
                    IsLoaded = true;
                });
            });

            //Task.Run(() =>
            //    {
            //        Thread.Sleep(100);
            //        foreach (var info in collection)
            //        {
            //            var a = info.Image;
            //        }
            //    }
            //);
        }

        // ==================================================================================================================
        #endregion


        static IEnumerable<string> DirSearch(string sDir, params string[] types)
        {
            var list = new List<string>();
            try
            {
                foreach (string f in Directory.GetFiles(sDir))
                {
                    var type = Path.GetExtension(f);
                    if (types.Any(o => o.Equals(type)))
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

    public class ImageInfo /*: DependencyObject*/
    {
        private static object mutex = new object();

        //public static BitmapImage CreateThumbnail(string imagePath)
        //{
        //    BitmapImage bitmap = new BitmapImage();

        //    lock (mutex)
        //    {
        //        using (var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read))
        //        {
        //            bitmap.BeginInit();
        //            bitmap.DecodePixelWidth = 283;
        //            bitmap.DecodePixelHeight = 283;
        //            bitmap.CacheOption = BitmapCacheOption.None;
        //            bitmap.StreamSource = stream;
        //            bitmap.EndInit();
        //        }

        //        bitmap.Freeze();
        //    }


        //    //GC.WaitForPendingFinalizers();
        //    //GC.Collect();

        //    return bitmap;
        //}

        private BitmapImage _image;

        //public async Task<BitmapImage> UpdateImage()
        //{
        //     return await Task.Run(() => CreateThumbnail(FilePath));
        //}

        //public static readonly DependencyProperty ImageProperty = DependencyProperty.Register("Image", typeof(BitmapImage), typeof(ImageInfo), new PropertyMetadata(default(BitmapImage)));
        public BitmapImage Image
        {
            //get
            //{
            //    var image = UpdateImage().Result;
            //    return  image;
            //}
            get
            {
                if (_image == null)
                {


                    var DecodeSize = 200;
                    //var UriSource = new Uri(FilePath);

                    // Only load thumbnails
                    byte[] buffer = File.ReadAllBytes(FilePath);
                    var mem = new MemoryStream(buffer);
                    BitmapImage reducedImage = new BitmapImage();
                    reducedImage.BeginInit();
                    reducedImage.CacheOption = BitmapCacheOption.Default;
                    reducedImage.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    //_reducedImage.DecodePixelWidth = DecodeSize;
                    reducedImage.DecodePixelHeight = DecodeSize;
                    reducedImage.StreamSource = mem;
                    reducedImage.Rotation = Rotation.Rotate0;
                    reducedImage.EndInit();
                    //buffer = null;
                    reducedImage.Freeze();
                    _image = reducedImage;
                }

                return _image;
            }
            set
            {
                if (_image != value)
                {
                    _image = value;
                }
            }
        }

        private string _filePath;
        public string FilePath
        {
            get { return _filePath; }
            set
            {
                _filePath = value;
                //UpdateImage();
            }
        }
    }
}