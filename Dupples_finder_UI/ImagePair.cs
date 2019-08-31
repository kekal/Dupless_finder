using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace Dupples_finder_UI
{
    public class ImagePair : DisposableObject
    {
        public double Match;

        public ImageInfo Image1 { get; set; }

        public ImageInfo Image2 { get; set; }


        #region Disposing

        protected override void Clean()
        {
            Image1.Dispose();
            Image2.Dispose();
            Image1 = null;
            Image2 = null;
        }

        #endregion
    }

    // ==================================================================================================================


    public class ImageInfo : DisposableObject
    {
        
        private static readonly Semaphore Sem = new Semaphore(Environment.ProcessorCount, Environment.ProcessorCount);
        public string FilePath { get; set; }

        private BitmapImage _image;
        public BitmapImage Image
        {
            get
            {
                if (Environment.StackTrace.Contains("Clr"))
                {
                    Trace.WriteLine($"Main thread (id {Thread.CurrentThread.ManagedThreadId}) requested {Path.GetFileName(FilePath)}");
                }
                else
                {
                    Trace.WriteLine($"Background thread {Thread.CurrentThread.ManagedThreadId} loads new {Path.GetFileName(FilePath)}");
                }

                
                if (_image == null)
                {
                    if (!Environment.StackTrace.Contains("Clr"))
                    {
                        Thread.CurrentThread.Priority = ThreadPriority.Lowest;
                        Thread.Sleep(1);
                    }

                    Trace.WriteLine($"     {Path.GetFileName(FilePath)} had been loaded from disk");
                    _image = Laz.Value;

                    return _image;
                }
                Trace.WriteLine($"     {Path.GetFileName(FilePath)} had been already loaded");
                return _image;
            }
        }

        public ImageInfo(string path)
        {
            FilePath = path;
            Trace.WriteLine($"{Thread.CurrentThread.ManagedThreadId} started to load {Path.GetFileName(FilePath)}");

            Laz = new Lazy<BitmapImage>(() => LoadImage(FilePath));
            PrepareCommands();
        }

        public RelayCommand ImageClick { get; private set; }
        private void PrepareCommands()
        {
            ImageClick = new RelayCommand(() => Process.Start(FilePath));
        }

        private Lazy<BitmapImage> Laz { get; set; }

        private static BitmapImage LoadImage(string path)
        {
            double decodeSize = MainViewModel.Inst.ThumbnailSize;

            Sem.WaitOne();
            var sourceMat = new Mat(path);
            Sem.Release();
            if (sourceMat.Width < 1 || sourceMat.Height < 1)
            {
                sourceMat.Release();
                sourceMat = Mat.Zeros(new Size(200, 200), MatType.CV_8UC3);
            }

            var scale = Math.Min(decodeSize / sourceMat.Width, decodeSize / sourceMat.Height);
            var resizedMat = sourceMat.Resize(new Size(0, 0), scale, scale, InterpolationFlags.Area);

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