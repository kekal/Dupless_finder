using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace Dupples_finder_UI
{
    public class ImagePair : DisposableObject
    {
        public double Match;
        private ImageInfo _image1;
        private ImageInfo _image2;

        public ImageInfo Image1
        {
            get { return _image1; }
            set { _image1 = value; }
        }

        public ImageInfo Image2
        {
            get { return _image2; }
            set { _image2 = value; }
        }



        #region Disposing

        protected override void Clean()
        {
            _image1.Dispose();
            _image2.Dispose();
            _image1 = null;
            _image2 = null;
        }

        #endregion
    }

    // ==================================================================================================================


    public class ImageInfo : DisposableObject
    {
        private static readonly Semaphore Sem = new Semaphore(Environment.ProcessorCount - 1, Environment.ProcessorCount - 1);
        public string FilePath { get; set; }

        private BitmapImage _image;
        public SortedList SortedList = SortedList.Synchronized(new SortedList(new Comparer(CultureInfo.CurrentCulture))) ;

        public BitmapImage Image
        {
            get
            {
                if (Environment.StackTrace.Contains("Clr"))
                {
                    Trace.WriteLine($"Main thread requested {Path.GetFileName(FilePath)}");
                }
                else
                {
                    Trace.WriteLine($"{Thread.CurrentThread.ManagedThreadId} loads new {Path.GetFileName(FilePath)}");
                }


                if (_image == null)
                {
                    if (Environment.StackTrace.Contains("Clr"))
                    {
                        Thread.CurrentThread.Priority = ThreadPriority.Lowest;
                        Thread.Sleep(1);
                        _image = Laz.Value;
                        return _image;
                    }
                    
                }
                Trace.WriteLine($"{Thread.CurrentThread.ManagedThreadId} gets already loaded {Path.GetFileName(FilePath)}");
                return _image;
            }
        }

        public ImageInfo(string path)
        {
            FilePath = path;
            Trace.WriteLine($"{Thread.CurrentThread.ManagedThreadId} started to load {Path.GetFileName(FilePath)}");

            Laz = new Lazy<BitmapImage>(() =>
                {
                    return LoadImage(FilePath);
                }
                /*, LazyThreadSafetyMode.None*/);
            PrepareCommands();
        }

        public RelayCommand ImageClick { get; private set; }
        private void PrepareCommands()
        {
            ImageClick = new RelayCommand(() => Process.Start(FilePath));
        }

        public Lazy<BitmapImage> Laz { get; private set; }

        private static BitmapImage LoadImage(string path)
        {
            double decodeSize = MainViewModel.Inst.ThumbnailSize;

            Sem.WaitOne();
            var sourceMat = new Mat(path);
            Sem.Release();

            var scale = Math.Min(decodeSize / sourceMat.Width, decodeSize / sourceMat.Height);
            var resizedMat = sourceMat.Resize(new OpenCvSharp.Size(0, 0), scale, scale, InterpolationFlags.Area);

            var reducedImage = new BitmapImage();
            reducedImage.BeginInit();
            reducedImage.StreamSource = resizedMat.ToMemoryStream(".jpg",
                new ImageEncodingParam(ImwriteFlags.JpegQuality, 95),
                new ImageEncodingParam(ImwriteFlags.JpegProgressive, 1),
                new ImageEncodingParam(ImwriteFlags.JpegOptimize, 1));

            //reducedImage.StreamSource = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            reducedImage.DecodePixelHeight = MainViewModel.Inst.ThumbnailSize;
            reducedImage.CacheOption = BitmapCacheOption.None;
            reducedImage.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            reducedImage.Rotation = Rotation.Rotate0;

            reducedImage.EndInit();
            reducedImage.Freeze();

            resizedMat.Release();
            sourceMat.Release();

            return reducedImage;
        }
        
        #region disposing

        protected override void Clean()
        {
            Laz = null;
        }
        #endregion
    }
}