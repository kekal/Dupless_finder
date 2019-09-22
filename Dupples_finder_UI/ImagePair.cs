using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using Size = OpenCvSharp.Size;

namespace Dupples_finder_UI
{
    public class ImagePair : DependencyObject, IDisposable
    {

        public double Match { get; set; }


        public ImageInfo Image1 { get; set; }

        public ImageInfo Image2 { get; set; }

        public string MatchString => Match.ToString("F");


        public void Dispose()
        {
            Image1.Dispose();
            Image2.Dispose();
            Image1 = null;
            Image2 = null;
        }
    }

    // ==================================================================================================================


    public class ImageInfo : DisposableObject
    {
        
        private static readonly Semaphore Sem = new Semaphore(Environment.ProcessorCount, Environment.ProcessorCount);
        private static object Lock = new object();
        public string FilePath { get; set; }

        private ImageSource _image;
        private Mat _storedMat;

        public Mat StoredMat
        {
            get
            {
                if (_storedMat == null)
                {
                    StoreMat();
                }
                return _storedMat;
            }
            private set => _storedMat = value;
        }

        public ImageSource Image
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

                    return Laz.Value;
                }
                Trace.WriteLine($"     {Path.GetFileName(FilePath)} had been already loaded");
                return _image;
            }
        }

        public ImageInfo(string path)
        {
            FilePath = path;
            Trace.WriteLine($"{Thread.CurrentThread.ManagedThreadId} started to load {Path.GetFileName(FilePath)}");

            Laz = new Lazy<ImageSource>(() => LoadImage(FilePath));
            PrepareCommands();
        }

        public RelayCommand ImageClick { get; private set; }
        private void PrepareCommands()
        {
            ImageClick = new RelayCommand(() => Process.Start(FilePath));
        }

        private Lazy<ImageSource> Laz { get; set; }

        private ImageSource LoadImage(string path)
        {
            if (_storedMat == null || _image == null)
            {
                Sem.WaitOne();
                StoreMat();
                Sem.Release();
            }

            //BitmapImage reducedImage = new BitmapImage();
            //reducedImage.BeginInit();

            ImageSource reducedImage = OpenCvSharp.Extensions.BitmapSourceConverter.ToBitmapSource(StoredMat);


            //MemoryStream dfg = StoredMat.ToMemoryStream(".jpg",
            //    new ImageEncodingParam(ImwriteFlags.JpegQuality, 95),
            //    new ImageEncodingParam(ImwriteFlags.JpegProgressive, 1),
            //    new ImageEncodingParam(ImwriteFlags.JpegOptimize, 1));
            //reducedImage.StreamSource = dfg;

            //reducedImage.StreamSource = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            //reducedImage.DecodePixelHeight = MainViewModel.Inst.ThumbnailSize;
            //reducedImage.CacheOption = BitmapCacheOption.None;
            //reducedImage.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            //reducedImage.Rotation = Rotation.Rotate0;

            //reducedImage.EndInit();
            reducedImage.Freeze();

            _image = reducedImage;
            return _image;
        }

        private void StoreMat()
        {
            if (_storedMat != null)
            {
                return;
            }

            double decodeSize = MainViewModel.Inst.ThumbnailSize;
            var sourceMat = new Mat(FilePath);

            if (sourceMat?.Width < 1 || sourceMat?.Height < 1)
            {
                sourceMat?.Release();
                lock (Lock)
                {
                    _storedMat?.Release();
                    _storedMat = Mat.Zeros(new Size(decodeSize, decodeSize), MatType.CV_8UC3);
                }

                _image = new BitmapImage();
                return;
            }

            var scale = Math.Min(decodeSize / sourceMat.Width, decodeSize / sourceMat.Height);
            var resized = sourceMat.Resize(new Size(0, 0), scale, scale, InterpolationFlags.Area);

            lock (Lock)
            {
                _storedMat?.Release();
                _storedMat = resized;
            }

            //resized.Release();
            sourceMat.Release();
        }

        #region disposing

        protected override void Clean()
        {
            Laz = null;
        }
        #endregion
    }
}