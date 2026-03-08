using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Dupples_finder_UI.Services.Interfaces;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;

namespace Dupples_finder_UI.Services;

public class ThumbnailService : IThumbnailService
{
    /// <summary>
    /// IID for IShellItemImageFactory, used by SHCreateItemFromParsingName.
    /// </summary>
    private static readonly Guid ShellItemImageFactoryGuid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    /// <summary>
    /// SIIGBF flags that control how IShellItemImageFactory.GetImage produces the thumbnail.
    /// </summary>
    [Flags]
    private enum SIIGBF
    {
        /// <summary>Resize the image to fit within the requested size, maintaining aspect ratio.</summary>
        SIIGBF_RESIZETOFIT = 0x00,
        /// <summary>Allow returning an image larger than the requested size.</summary>
        SIIGBF_BIGGERSIZEOK = 0x01,
        /// <summary>Only return the image if it is already cached in memory.</summary>
        SIIGBF_MEMORYONLY = 0x02,
        /// <summary>Only return the icon, never the thumbnail.</summary>
        SIIGBF_ICONONLY = 0x04,
        /// <summary>Only return a cached or embedded thumbnail; never generate from the full image.</summary>
        SIIGBF_THUMBNAILONLY = 0x08
    }

    /// <summary>
    /// COM interface for obtaining thumbnails from Shell items.
    /// GUID: bcc18b79-ba16-442f-80c4-8a59c30c463b
    /// </summary>
    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    /// <summary>
    /// SIZE structure matching the Win32 SIZE used by GetImage.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;

        public SIZE(int cx, int cy)
        {
            this.cx = cx;
            this.cy = cy;
        }
    }

    public BitmapSource BytesToBitmapSource(byte[] jpegBytes)
    {
        if (jpegBytes == null || jpegBytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(jpegBytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            PerfLogger.Verbose($"ThumbnailService.BytesToBitmapSource skipped: {ex.Message}");
            return null;
        }
    }

    public byte[] EncodeBitmapSourceToBytes(BitmapSource source)
    {
        if (source == null)
        {
            return [];
        }

        try
        {
            return EncodeBitmapSource(source);
        }
        catch (Exception ex)
        {
            PerfLogger.Log($"ThumbnailService.EncodeBitmapSourceToBytes failed: {ex.Message}");
            return [];
        }
    }

    public BitmapSource GetThumbnail(string filePath, int size = 200)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        using var op = PerfLogger.TimedVerbose("SHELL_API", minMs: 100);
        op.Detail($"file={filePath}");
        var hBitmap = IntPtr.Zero;
        try
        {
            hBitmap = GetHBitmap(filePath, size);
            if (hBitmap == IntPtr.Zero)
            {
                op.Detail("MISS");
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            source.Freeze();
            op.Detail($"HIT | px={source.PixelWidth}x{source.PixelHeight}");
            return source;
        }
        catch (Exception ex)
        {
            op.Suppress();
            PerfLogger.Log($"ThumbnailService.GetThumbnail failed for '{filePath}': {ex.Message}");
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                DeleteObject(hBitmap);
            }
        }
    }

    /// <summary>
    /// Deletes a GDI object (HBITMAP) to prevent GDI handle leaks.
    /// </summary>
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    private static byte[] EncodeBitmapSource(BitmapSource source)
    {
        // Try JPEG first (smaller output)
        try
        {
            var jpegEncoder = new JpegBitmapEncoder();
            jpegEncoder.QualityLevel = 85;
            jpegEncoder.Frames.Add(BitmapFrame.Create(source));

            using var ms = new MemoryStream();
            jpegEncoder.Save(ms);
            return ms.ToArray();
        }
        catch (Exception jpegEx)
        {
            PerfLogger.Log($"ThumbnailService: JPEG encoding failed, trying PNG. Error: {jpegEx.Message}");
        }

        // Fallback to PNG
        try
        {
            var pngEncoder = new PngBitmapEncoder();
            pngEncoder.Frames.Add(BitmapFrame.Create(source));

            using var ms = new MemoryStream();
            pngEncoder.Save(ms);
            return ms.ToArray();
        }
        catch (Exception pngEx)
        {
            PerfLogger.Log($"ThumbnailService: PNG encoding also failed. Error: {pngEx.Message}");
            return [];
        }
    }

    /// <summary>
    /// Extracts the embedded EXIF/JFIF thumbnail from the file header.
    /// Reads only the first ~256 KB of the file, making it ideal for network shares
    /// where reading the full multi-MB image would saturate the link.
    /// Returns null if the format has no embedded thumbnail (e.g. PNG, BMP).
    /// </summary>
    public BitmapSource GetEmbeddedThumbnail(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        using var op = PerfLogger.TimedVerbose("EXIF", minMs: 100);
        op.Detail($"file={filePath}");
        try
        {
            byte[] headerBuffer;
            int bytesRead;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var readSize = (int)Math.Min(fs.Length, 256 * 1024);
                headerBuffer = new byte[readSize];
                bytesRead = fs.Read(headerBuffer, 0, readSize);
                if (bytesRead < readSize)
                {
                    Array.Resize(ref headerBuffer, bytesRead);
                }
            }
            op.Lap("read");

            using var ms = new MemoryStream(headerBuffer);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            op.Lap("decode");
            op.Detail($"bytesRead={bytesRead}");

            if (decoder.Frames?.Count > 0)
            {
                var thumb = decoder.Frames[0].Thumbnail;
                if (thumb != null)
                {
                    if (!thumb.IsFrozen)
                    {
                        thumb.Freeze();
                    }

                    op.Detail($"HIT | px={thumb.PixelWidth}x{thumb.PixelHeight}");
                    return thumb;
                }
            }

            op.Detail("NO_THUMB");
        }
        catch (Exception ex)
        {
            op.Detail($"FAILED | error={ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Extracts the embedded EXIF thumbnail as raw JPEG bytes using MetadataExtractor.
    /// Pure managed code — fully thread-safe, runs in true parallel.
    /// Reads only ~256KB of the file header from disk.
    /// </summary>
    public byte[] GetEmbeddedThumbnailBytes(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        using var op = PerfLogger.TimedVerbose("EXIF_RAW");
        op.Detail($"file={filePath}");
        try
        {
            byte[] header;
            int bytesRead;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var readSize = (int)Math.Min(fs.Length, 256 * 1024);
                if (readSize < 12) { op.Suppress(); return null; }
                header = new byte[readSize];
                bytesRead = fs.Read(header, 0, readSize);
            }
            op.Lap("read");

            byte[] thumbBytes = null;
            using (var ms = new MemoryStream(header, 0, bytesRead))
            {
                var directories = JpegMetadataReader.ReadMetadata(ms);
                var thumbDir = directories.OfType<ExifThumbnailDirectory>().FirstOrDefault();
                if (thumbDir != null)
                {
                    var adjustedOffset = thumbDir.AdjustedThumbnailOffset;
                    if (adjustedOffset.HasValue &&
                        thumbDir.TryGetInt32(ExifThumbnailDirectory.TagThumbnailLength, out var length) &&
                        length > 0 && length <= 200_000 &&
                        adjustedOffset.Value >= 0 &&
                        adjustedOffset.Value + length <= bytesRead)
                    {
                        thumbBytes = new byte[length];
                        Buffer.BlockCopy(header, adjustedOffset.Value, thumbBytes, 0, length);
                    }
                }
            }
            op.Lap("parse");

            op.Detail($"bytesRead={bytesRead}");
            op.Detail($"thumbSize={thumbBytes?.Length ?? 0}");
            op.Detail(thumbBytes != null ? "HIT" : "MISS");

            // Only log fast misses when slow (>100ms); always log hits
            if (thumbBytes == null && op.ElapsedMs <= 100)
            {
                op.Suppress();
            }

            return thumbBytes;
        }
        catch (Exception ex)
        {
            op.Detail($"FAILED | error={ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Obtains an HBITMAP from the Windows Shell for the given file.
    /// Only uses THUMBNAILONLY | BIGGERSIZEOK for maximum speed (cached/embedded Shell thumbnails).
    /// Does NOT fall back to RESIZETOFIT to avoid full-file reads over network shares.
    /// </summary>
    private static IntPtr GetHBitmap(string filePath, int size)
    {
        IShellItemImageFactory factory = null;
        try
        {
            SHCreateItemFromParsingName(filePath, IntPtr.Zero, ShellItemImageFactoryGuid, out factory);
            if (factory == null)
            {
                return IntPtr.Zero;
            }

            var nativeSize = new SIZE(size, size);

            // Fast path only — cached/embedded Shell thumbnails.
            // RESIZETOFIT is intentionally omitted: it reads the full image file,
            // which saturates network links for remote folders.
            var hr = factory.GetImage(nativeSize,
                SIIGBF.SIIGBF_THUMBNAILONLY | SIIGBF.SIIGBF_BIGGERSIZEOK,
                out var hBitmap);

            if (hr == 0 && hBitmap != IntPtr.Zero)
            {
                return hBitmap;
            }

            return IntPtr.Zero;
        }
        catch (COMException ex)
        {
            PerfLogger.Log($"ThumbnailService: COM error for '{filePath}': {ex.Message}");
            return IntPtr.Zero;
        }
        catch (FileNotFoundException)
        {
            // File was deleted between our check and the Shell call
            return IntPtr.Zero;
        }
        catch (Exception ex)
        {
            PerfLogger.Log($"ThumbnailService: Unexpected error for '{filePath}': {ex.Message}");
            return IntPtr.Zero;
        }
        finally
        {
            if (factory != null)
            {
                Marshal.ReleaseComObject(factory);
            }
        }
    }

    /// <summary>
    /// Creates a Shell item object from a file system path.
    /// </summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);
}