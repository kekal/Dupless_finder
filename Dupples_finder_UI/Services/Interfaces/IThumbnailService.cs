using System.Windows.Media.Imaging;

namespace Dupples_finder_UI.Services.Interfaces;

public interface IThumbnailService
{
    BitmapSource BytesToBitmapSource(byte[] jpegBytes);
    byte[] EncodeBitmapSourceToBytes(BitmapSource source);

    /// <summary>
    /// Gets a thumbnail via the Windows Shell API.
    /// Only uses THUMBNAILONLY (cached/embedded Shell thumbnails).
    /// Does NOT fall back to RESIZETOFIT to avoid full file reads over network.
    /// </summary>
    BitmapSource GetThumbnail(string filePath, int size = 200);

    /// <summary>
    /// Extracts the embedded EXIF/JFIF thumbnail from the file header.
    /// Reads only ~256KB of the file — ideal for network shares.
    /// Returns null if no embedded thumbnail exists.
    /// Uses WPF BitmapDecoder internally — may have thread affinity limitations.
    /// </summary>
    BitmapSource GetEmbeddedThumbnail(string filePath);

    /// <summary>
    /// Extracts the embedded EXIF thumbnail as raw JPEG bytes using pure byte parsing.
    /// Fully thread-safe — no WPF/COM dependencies, runs in true parallel.
    /// Reads only ~256KB of the file header from disk.
    /// Returns null if no embedded JPEG thumbnail exists.
    /// </summary>
    byte[] GetEmbeddedThumbnailBytes(string filePath);
}