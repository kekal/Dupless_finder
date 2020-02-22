using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using OpenCvSharp;

namespace Dupples_finder_UI
{
    public class ImagePair : DependencyObject, IDisposable
    {
        public static readonly DependencyProperty ThumbnailSizeProperty = DependencyProperty.Register("ThumbnailSize", typeof(ushort), typeof(ImagePair), new PropertyMetadata(default(ushort)));

        public double Match { private get; set; }
        public string BestDistance => Match.ToString("F");

        public ImageInfo Image1 { get; set; }

        public ImageInfo Image2 { get; set; }

        public ushort ThumbnailSize
        {
            get => (ushort) GetValue(ThumbnailSizeProperty);
            set => SetValue(ThumbnailSizeProperty, value);
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