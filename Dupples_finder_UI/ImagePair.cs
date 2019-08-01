using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace Dupples_finder_UI
{
    public class ImagePair : IDisposable
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

        public void Dispose()
        {
            _image1.Dispose();
            _image2.Dispose();
        }
    }

    // ==================================================================================================================


    public class ImageInfo : IDisposable
    {
        private static readonly object Lock1 = new object();

        private static Semaphore sem = new Semaphore(Environment.ProcessorCount - 1, Environment.ProcessorCount - 1);
        //static Semaphore sem = new Semaphore(2, 2);

        public ImageInfo()
        {
            PrepareCommands();
        }

        private void PrepareCommands()
        {
            ImageClick = new RelayCommand(() => Process.Start(FilePath));
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

                // Only load thumbnails
                _buffer = null;
                double decodeSize = MainViewModel.Inst.ThumbnailSize;

                MemoryStream mem;
                Mat mat;
                //lock (Lock1)
                //{
                sem.WaitOne();
                if (_image != null)
                {
                    sem.Release();
                    return _image;
                }
                mat = new Mat(FilePath);
                sem.Release();
                //}
                var scale = Math.Min(decodeSize / mat.Width, decodeSize / mat.Height);
                var resizedMat = mat.Resize(new OpenCvSharp.Size(0, 0), scale, scale, InterpolationFlags.Area);
                //mem = resizedMat.ToMemoryStream(".jpg");
                _buffer = resizedMat.ToBytes(".jpg");
                resizedMat?.Release();
                mat?.Dispose();

                //_buffer = File.ReadAllBytes(FilePath);

                mem = new MemoryStream(_buffer);

                var reducedImage = new BitmapImage();
                reducedImage.BeginInit();
                reducedImage.DecodePixelHeight = (int) decodeSize;
                reducedImage.CacheOption = BitmapCacheOption.OnLoad;
                reducedImage.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                //_reducedImage.DecodePixelWidth = DecodeSize;
                reducedImage.Rotation = Rotation.Rotate0;
                reducedImage.StreamSource = mem;
                //reducedImage.StreamSource = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                reducedImage.EndInit();
                reducedImage.Freeze();

                //mem?.Close();
                //_buffer = null;
                _image = reducedImage;
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

        public void Dispose()
        {
            Image = null;
        }
    }
}