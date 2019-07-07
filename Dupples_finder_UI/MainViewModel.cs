using System;
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

namespace Dupples_finder_UI
{
    public class MainViewModel : DependencyObject
    {
        private static MainViewModel _inst;

        public MainViewModel()
        {
            IsLoaded = true;
            PrepareCommands();
            _inst = this;
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
        public RelayCommand LoadCommad { get; private set; }

        private void PrepareCommands()
        {
            LoadCommad = new RelayCommand(LoadRoutine);

        }

        // ==================================================================================================================
        #endregion

        private void LoadRoutine()
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
        }

        private void LoadCollectionToMemory(IList<ImageInfo> collection)
        {
            Task.Factory.StartNew(() =>
            {
                Thread.Sleep(100);
                _inst.Dispatcher.Invoke(() => IsLoaded = false);

                foreach (var info in collection)
                {
                    var a = info.Image;
                }
            }).ContinueWith(e =>
            {
                _inst.Dispatcher.Invoke(() => IsLoaded = true);
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
            return _inst.DataCollection.FirstOrDefault(x => x.FilePath == (string) image.Tag);
        }
    }

    public class ImageInfo
    {
        public ImageInfo()
        {
            PrepareCommands();
        }

        public RelayCommand ImageClick { get; private set; }

        private BitmapImage _image;
        public BitmapImage Image
        {
            get
            {
                if (_image == null)
                {
                    var DecodeSize = 200;
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
                if (!ReferenceEquals(_image, value))
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
            }
        }

        private void PrepareCommands()
        {
            ImageClick = new RelayCommand(OpenImageRoutine);
        }

        private void OpenImageRoutine()
        {
            Process.Start(FilePath);
        }
    }
}