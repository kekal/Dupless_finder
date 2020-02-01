using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace Dupples_finder_UI
{
    /// <summary>  Class that contains image bitmap, helpers to operate with and information abour class </summary>
    public class ImageInfo : DisposableObject
    {
        /// <summary>  Semaphore for limited access to the file that belongs to this instance. (for the purposes of controled HDD load) </summary>
        private static readonly Semaphore Sem = new Semaphore(Environment.ProcessorCount, Environment.ProcessorCount);

        /// <summary> An object that garantie that only thread will operate with bitmap inside this class  </summary>
        private static readonly object Lock = new object();
        public string FilePath { get; set; }

        public string FileName => FilePath.Split('\\').LastOrDefault();


        //private string _fileName;

        private Mat _storedMat;
        /// <summary>  OpenCv bitmap instance for storing little thumbnail.  </summary>
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
            //private set => _storedMat = value;
        }

        private ImageSource _image;
        /// <summary> Actual bitmap used on UI. Have a singleton behaviour. The backfield will be filled up during the <c>LoadImage()</c> </summary>
        public ImageSource Image
        {
            get
            {
                Trace.WriteLine(Environment.StackTrace.Contains("Clr")
                    ? $"Main thread (id {Thread.CurrentThread.ManagedThreadId}) requested {Path.GetFileName(FilePath)}"
                    : $"Background thread {Thread.CurrentThread.ManagedThreadId} loads new {Path.GetFileName(FilePath)}");


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

            Laz = new Lazy<ImageSource>(() => LoadImage(/*FilePath*/)); // creating the connection for lazy loading of the bitmap.
            PrepareCommands();
        }

        /// <summary> The command that rises during the doubleClick on thumbnail in colletion view control </summary>
        public RelayCommand ImageDoubleClick { get; private set; }
        public RelayCommand ImageClick { get; private set; }

        /// <summary>  Method will create commands fot thumbnail view element  </summary>
        private void PrepareCommands()
        {
            
            ImageDoubleClick = new RelayCommand(() =>
            {
                MainViewModel.Inst.Openview(FilePath);
            });

            ImageClick = new RelayCommand(() => Process.Start(FilePath));
        }

        /// <summary> Lazy container for <c>Image</c> property </summary>
        private Lazy<ImageSource> Laz { get; set; }

        /// <summary> Method calls <c>StoreMat()</c> simultaneously with controled by semaphore way to reduce the load on HDD </summary>
        /// <returns>A bitmap when <c>Image</c> getter called</returns>
        private ImageSource LoadImage(/*string path*/)
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

            if (sourceMat.Width < 1 || sourceMat.Height < 1)
            {
                sourceMat.Release();
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