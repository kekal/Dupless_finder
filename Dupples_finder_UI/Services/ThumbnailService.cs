using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Dupples_finder_UI.Services.Interfaces;

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
            Trace.WriteLine($"ThumbnailService.BytesToBitmapSource failed: {ex.Message}");
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
            Trace.WriteLine($"ThumbnailService.EncodeBitmapSourceToBytes failed: {ex.Message}");
            return [];
        }
    }

    public BitmapSource GetThumbnail(string filePath, int size = 200)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        var hBitmap = IntPtr.Zero;
        try
        {
            hBitmap = GetHBitmap(filePath, size);
            if (hBitmap == IntPtr.Zero)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            source.Freeze();
            return source;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ThumbnailService.GetThumbnail failed for '{filePath}': {ex.Message}");
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
            Trace.WriteLine($"ThumbnailService: JPEG encoding failed, trying PNG. Error: {jpegEx.Message}");
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
            Trace.WriteLine($"ThumbnailService: PNG encoding also failed. Error: {pngEx.Message}");
            return [];
        }
    }

    /// <summary>
    /// Obtains an HBITMAP from the Windows Shell for the given file.
    /// First tries THUMBNAILONLY | BIGGERSIZEOK for maximum speed (cached/embedded thumbnails only).
    /// Falls back to RESIZETOFIT which may read the full file but still uses the Shell pipeline.
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

            // First attempt: fast path -- only use cached/embedded thumbnails
            var hr = factory.GetImage(nativeSize,
                SIIGBF.SIIGBF_THUMBNAILONLY | SIIGBF.SIIGBF_BIGGERSIZEOK,
                out var hBitmap);

            if (hr == 0 && hBitmap != IntPtr.Zero)
            {
                return hBitmap;
            }

            // Second attempt: allow the Shell to read and resize the full image
            hr = factory.GetImage(nativeSize, SIIGBF.SIIGBF_RESIZETOFIT, out hBitmap);

            if (hr == 0 && hBitmap != IntPtr.Zero)
            {
                return hBitmap;
            }

            Trace.WriteLine($"ThumbnailService: Shell GetImage failed for '{filePath}', HRESULT=0x{hr:X8}");
            return IntPtr.Zero;
        }
        catch (COMException ex)
        {
            Trace.WriteLine($"ThumbnailService: COM error for '{filePath}': {ex.Message}");
            return IntPtr.Zero;
        }
        catch (FileNotFoundException)
        {
            // File was deleted between our check and the Shell call
            return IntPtr.Zero;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ThumbnailService: Unexpected error for '{filePath}': {ex.Message}");
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