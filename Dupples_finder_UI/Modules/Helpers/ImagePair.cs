using System;
using System.Windows;
using Dupples_finder_UI.DTO;
using Dupples_finder_UI.Events;
using Prism.Commands;
using Prism.Events;

namespace Dupples_finder_UI.Modules.Helpers
{
    public class ImagePair : DependencyObject, IDisposable
    {
        public static readonly DependencyProperty ThumbnailSizeProperty = DependencyProperty.Register(nameof(ThumbnailSize), typeof(ushort), typeof(ImagePair), new PropertyMetadata(default(ushort)));

        public double Match { private get; set; }
        public string BestDistance => Match.ToString("F");

        public ImageInfo Image1 { get; set; }

        public ImageInfo Image2 { get; set; }

        /// <summary>Opens Image1 in preview with Image2 as the alternate (mouse-press) image.</summary>
        public DelegateCommand Image1DoubleClick { get; private set; }

        /// <summary>Opens Image2 in preview with Image1 as the alternate (mouse-press) image.</summary>
        public DelegateCommand Image2DoubleClick { get; private set; }

        public ushort ThumbnailSize
        {
            get => (ushort) GetValue(ThumbnailSizeProperty);
            set => SetValue(ThumbnailSizeProperty, value);
        }

        public ImagePair(ushort thumbnailSize, IEventAggregator eventAggregator)
        {
            ThumbnailSize = thumbnailSize;

            Image1DoubleClick = new DelegateCommand(() =>
            {
                eventAggregator?.GetEvent<OpenImagePreviewEvent>()
                    .Publish(new OpenImagePreviewPayload
                    {
                        FilePath = Image1?.FilePath,
                        AlternatePath = Image2?.FilePath
                    });
            });

            Image2DoubleClick = new DelegateCommand(() =>
            {
                eventAggregator?.GetEvent<OpenImagePreviewEvent>()
                    .Publish(new OpenImagePreviewPayload
                    {
                        FilePath = Image2?.FilePath,
                        AlternatePath = Image1?.FilePath
                    });
            });
        }

        public void Dispose()
        {
            Image1?.Dispose();
            Image2?.Dispose();
            Image1 = null;
            Image2 = null;
        }
    }
}
