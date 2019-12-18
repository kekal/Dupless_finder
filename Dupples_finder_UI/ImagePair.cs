using System;
using System.Windows;

namespace Dupples_finder_UI
{
    public class ImagePair : DependencyObject, IDisposable
    {
        public static readonly DependencyProperty ThumbnailSizeProperty = DependencyProperty.Register("ThumbnailSize", typeof(ushort), typeof(ImagePair), new PropertyMetadata(default(ushort)));

        public double Match { get; set; }
        public float[] MatchPoints { get; set; }
        public string MatchString => Match.ToString("F");

        public ImageInfo Image1 { get; set; }

        public ImageInfo Image2 { get; set; }

        public ushort ThumbnailSize
        {
            get { return (ushort) GetValue(ThumbnailSizeProperty); }
            set { SetValue(ThumbnailSizeProperty, value); }
        }

        public ImagePair(ushort thumbnailSize)
        {
            ThumbnailSize = thumbnailSize;
        }



        public void Dispose()
        {
            Image1.Dispose();
            Image2.Dispose();
            Image1 = null;
            Image2 = null;
        }
    }

    // ==================================================================================================================
}