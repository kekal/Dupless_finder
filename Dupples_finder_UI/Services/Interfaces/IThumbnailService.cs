using System.Windows.Media.Imaging;

namespace Dupples_finder_UI.Services.Interfaces
{
    public interface IThumbnailService
    {
        BitmapSource GetThumbnail(string filePath, int size = 200);
        BitmapSource BytesToBitmapSource(byte[] jpegBytes);
        byte[] EncodeBitmapSourceToBytes(BitmapSource source);
    }
}
