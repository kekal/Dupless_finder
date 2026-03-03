using System;
using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;
using Dupples_finder_UI.Services;
using Xunit;

namespace Dupless_finder_Tests;

/// <summary>
/// xUnit tests for the ThumbnailService utility class.
/// Tests null-handling, empty-input, non-existent file handling, and round-trip encoding.
/// </summary>
public class ThumbnailServiceTests : IDisposable
{
    private readonly string _tempImagePath;
    private readonly ThumbnailService _service = new();

    public ThumbnailServiceTests()
    {
        _tempImagePath = Path.Combine(Path.GetTempPath(), $"test_image_{Guid.NewGuid()}.bmp");
    }

    public void Dispose()
    {
        if (File.Exists(_tempImagePath))
        {
            try
            {
                File.Delete(_tempImagePath);
            }
            catch
            {
                // Ignore cleanup failures
            }
        }
    }

    #region GetThumbnail Tests

    [Fact]
    public void GetThumbnail_ReturnsNull_ForNullPath()
    {
        // Act
        var result = _service.GetThumbnail(null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetThumbnail_ReturnsNull_ForEmptyPath()
    {
        // Act
        var result = _service.GetThumbnail(string.Empty);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetThumbnail_ReturnsNull_ForNonExistentFile()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.jpg");

        // Act
        var result = _service.GetThumbnail(nonExistentPath);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region BytesToBitmapSource Tests

    [Fact]
    public void BytesToBitmapSource_ReturnsNull_ForNullInput()
    {
        // Act
        var result = _service.BytesToBitmapSource(null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void BytesToBitmapSource_ReturnsNull_ForEmptyArray()
    {
        // Arrange
        var emptyBytes = Array.Empty<byte>();

        // Act
        var result = _service.BytesToBitmapSource(emptyBytes);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region EncodeBitmapSourceToBytes Tests

    [Fact]
    public void EncodeBitmapSourceToBytes_ReturnsEmpty_ForNullInput()
    {
        // Act
        var result = _service.EncodeBitmapSourceToBytes(null);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region Round-Trip Tests with Valid Image

    [Fact]
    public void GetThumbnail_ReturnsBitmapSource_ForValidImage()
    {
        // Arrange
        CreateTestBitmap(_tempImagePath, 10, 10);

        // Act
        BitmapSource result = null;
        var thread = new Thread(() =>
        {
            result = _service.GetThumbnail(_tempImagePath);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(0, result.PixelWidth);
        Assert.NotEqual(0, result.PixelHeight);
    }

    [Fact]
    public void EncodeBitmapSourceToBytes_RoundTrips_WithBytesToBitmapSource()
    {
        // Arrange
        CreateTestBitmap(_tempImagePath, 10, 10);

        BitmapSource source = null;
        byte[] bytes = null;
        BitmapSource result = null;

        var thread = new Thread(() =>
        {
            // Get thumbnail, encode to bytes, decode back
            source = _service.GetThumbnail(_tempImagePath);
            if (source != null)
            {
                bytes = _service.EncodeBitmapSourceToBytes(source);
                result = _service.BytesToBitmapSource(bytes);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        // Assert
        Assert.NotNull(source);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        Assert.NotNull(result);
        Assert.NotEqual(0, result.PixelWidth);
        Assert.NotEqual(0, result.PixelHeight);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a simple test bitmap at the specified path with the given dimensions.
    /// </summary>
    private static void CreateTestBitmap(string path, int width, int height)
    {
        using var bmp = new System.Drawing.Bitmap(width, height);
        // Fill with a simple pattern to ensure it's a valid image
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                bmp.SetPixel(x, y, System.Drawing.Color.Red);
            }
        }

        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Bmp);
    }

    #endregion
}