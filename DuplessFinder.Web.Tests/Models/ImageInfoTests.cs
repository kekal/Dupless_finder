using DuplessFinder.Web.Models;
using DuplessFinder.Web.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace DuplessFinder.Web.Tests.Models;

public class ImageInfoTests
{
    private static FileEntry CreateFileEntry(string path = "test/image.jpg", string name = "image.jpg", long size = 1000, long lastModified = 12345)
        => new(path, name, size, lastModified);

    #region Constructor and Properties Tests

    [Fact]
    public void Constructor_SetsPathFromFileEntry()
    {
        var entry = CreateFileEntry("path/to/image.jpg");
        var imageInfo = new ImageInfo(entry);

        imageInfo.Path.Should().Be("path/to/image.jpg");
    }

    [Fact]
    public void Constructor_SetsNameFromFileEntry()
    {
        var entry = CreateFileEntry(name: "myimage.jpg");
        var imageInfo = new ImageInfo(entry);

        imageInfo.Name.Should().Be("myimage.jpg");
    }

    [Fact]
    public void Constructor_SetsFileSizeFromFileEntry()
    {
        var entry = CreateFileEntry(size: 5000);
        var imageInfo = new ImageInfo(entry);

        imageInfo.FileSize.Should().Be(5000);
    }

    [Fact]
    public void Constructor_SetsLastModifiedFromFileEntry()
    {
        var entry = CreateFileEntry(lastModified: 67890);
        var imageInfo = new ImageInfo(entry);

        imageInfo.LastModified.Should().Be(67890);
    }

    [Fact]
    public void Fingerprint_ReturnsFileSize_LastModifiedFormat()
    {
        var entry = CreateFileEntry(size: 2500, lastModified: 55555);
        var imageInfo = new ImageInfo(entry);

        imageInfo.Fingerprint.Should().Be("2500_55555");
    }

    [Fact]
    public void ThumbnailDataUrl_InitiallyNull()
    {
        var entry = CreateFileEntry();
        var imageInfo = new ImageInfo(entry);

        imageInfo.ThumbnailDataUrl.Should().BeNull();
    }

    [Fact]
    public void IsThumbnailLoaded_InitiallyFalse()
    {
        var entry = CreateFileEntry();
        var imageInfo = new ImageInfo(entry);

        imageInfo.IsThumbnailLoaded.Should().BeFalse();
    }

    [Fact]
    public void Setting_ThumbnailDataUrl_SetsIsThumbnailLoadedToTrue()
    {
        var entry = CreateFileEntry();
        var imageInfo = new ImageInfo(entry);

        imageInfo.ThumbnailDataUrl = "data:image/jpeg;base64,/9j/4AAQSkZJRg==";

        imageInfo.IsThumbnailLoaded.Should().BeTrue();
    }

    [Fact]
    public void Setting_ThumbnailDataUrlToNull_SetsIsThumbnailLoadedToFalse()
    {
        var entry = CreateFileEntry();
        var imageInfo = new ImageInfo(entry);
        imageInfo.ThumbnailDataUrl = "data:image/jpeg;base64,/9j/4AAQSkZJRg==";

        imageInfo.ThumbnailDataUrl = null;

        imageInfo.IsThumbnailLoaded.Should().BeFalse();
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_SetsThumbnailDataUrlToNull()
    {
        var entry = CreateFileEntry();
        var imageInfo = new ImageInfo(entry);
        imageInfo.ThumbnailDataUrl = "data:image/jpeg;base64,test";

        imageInfo.Dispose();

        imageInfo.ThumbnailDataUrl.Should().BeNull();
    }

    #endregion

    #region LoadThumbnailAsync Tests

    [Fact]
    public async Task LoadThumbnailAsync_UsesCachedThumbnail_WhenAvailable()
    {
        var entry = CreateFileEntry();
        var imageInfo = new ImageInfo(entry);

        var cachedBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG header
        var expectedDataUrl = $"data:image/jpeg;base64,{Convert.ToBase64String(cachedBytes)}";

        var mockCache = new Mock<ICacheService>();
        mockCache.Setup(c => c.GetThumbnailAsync(imageInfo.Fingerprint))
            .ReturnsAsync(cachedBytes);

        var mockFileAccess = new Mock<IFileAccessService>();
        var mockOpenCv = new Mock<IOpenCvService>();

        await imageInfo.LoadThumbnailAsync(mockCache.Object, mockFileAccess.Object, mockOpenCv.Object);

        imageInfo.ThumbnailDataUrl.Should().Be(expectedDataUrl);
        imageInfo.IsThumbnailLoaded.Should().BeTrue();
        // Verify no file read was attempted (cache hit)
        mockFileAccess.Verify(f => f.ReadFileBytesAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LoadThumbnailAsync_GeneratesThumbnail_WhenCacheMiss()
    {
        var entry = CreateFileEntry();
        var imageInfo = new ImageInfo(entry);

        var fileBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 }; // JPEG header
        var generatedThumbBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x01 };
        var expectedDataUrl = $"data:image/jpeg;base64,{Convert.ToBase64String(generatedThumbBytes)}";

        var mockCache = new Mock<ICacheService>();
        mockCache.Setup(c => c.GetThumbnailAsync(It.IsAny<string>()))
            .ReturnsAsync((byte[]?)null);

        var mockFileAccess = new Mock<IFileAccessService>();
        mockFileAccess.Setup(f => f.ReadFileBytesAsync(imageInfo.Path))
            .ReturnsAsync(fileBytes);

        var mockOpenCv = new Mock<IOpenCvService>();
        mockOpenCv.Setup(o => o.GenerateThumbnailAsync(fileBytes, 200))
            .ReturnsAsync(generatedThumbBytes);

        await imageInfo.LoadThumbnailAsync(mockCache.Object, mockFileAccess.Object, mockOpenCv.Object, 200);

        imageInfo.ThumbnailDataUrl.Should().Be(expectedDataUrl);
        mockCache.Verify(c => c.PutThumbnailAsync(imageInfo.Fingerprint, generatedThumbBytes), Times.Once);
    }

    [Fact]
    public async Task LoadThumbnailAsync_FallsBackToCreateObjectUrl_WhenOpenCvReturnsEmpty()
    {
        var entry = CreateFileEntry();
        var imageInfo = new ImageInfo(entry);

        var fileBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var objectUrl = "blob:http://localhost:5000/12345";

        var mockCache = new Mock<ICacheService>();
        mockCache.Setup(c => c.GetThumbnailAsync(It.IsAny<string>()))
            .ReturnsAsync((byte[]?)null);

        var mockFileAccess = new Mock<IFileAccessService>();
        mockFileAccess.Setup(f => f.ReadFileBytesAsync(imageInfo.Path))
            .ReturnsAsync(fileBytes);
        mockFileAccess.Setup(f => f.CreateObjectUrlAsync(imageInfo.Path))
            .ReturnsAsync(objectUrl);

        var mockOpenCv = new Mock<IOpenCvService>();
        mockOpenCv.Setup(o => o.GenerateThumbnailAsync(It.IsAny<byte[]>(), It.IsAny<int>()))
            .ReturnsAsync(new byte[0]); // Empty thumbnail

        await imageInfo.LoadThumbnailAsync(mockCache.Object, mockFileAccess.Object, mockOpenCv.Object);

        imageInfo.ThumbnailDataUrl.Should().Be(objectUrl);
    }

    [Fact]
    public async Task LoadThumbnailAsync_FallsBackToCreateObjectUrl_WhenOpenCvServiceIsNull()
    {
        var entry = CreateFileEntry();
        var imageInfo = new ImageInfo(entry);

        var fileBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var objectUrl = "blob:http://localhost:5000/67890";

        var mockCache = new Mock<ICacheService>();
        mockCache.Setup(c => c.GetThumbnailAsync(It.IsAny<string>()))
            .ReturnsAsync((byte[]?)null);

        var mockFileAccess = new Mock<IFileAccessService>();
        mockFileAccess.Setup(f => f.ReadFileBytesAsync(imageInfo.Path))
            .ReturnsAsync(fileBytes);
        mockFileAccess.Setup(f => f.CreateObjectUrlAsync(imageInfo.Path))
            .ReturnsAsync(objectUrl);

        await imageInfo.LoadThumbnailAsync(mockCache.Object, mockFileAccess.Object, opencvService: null);

        imageInfo.ThumbnailDataUrl.Should().Be(objectUrl);
    }

    [Fact]
    public async Task LoadThumbnailAsync_ThrowsOperationCanceledException_WhenTokenCancelledBeforeStart()
    {
        var entry = CreateFileEntry();
        var imageInfo = new ImageInfo(entry);

        var mockCache = new Mock<ICacheService>();
        var mockFileAccess = new Mock<IFileAccessService>();

        var cts = new CancellationTokenSource();
        cts.Cancel();

        await imageInfo.Invoking(i => i.LoadThumbnailAsync(mockCache.Object, mockFileAccess.Object, ct: cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task LoadThumbnailAsync_ThrowsOperationCanceledException_WhenTokenCancelledAfterCacheCheck()
    {
        var entry = CreateFileEntry();
        var imageInfo = new ImageInfo(entry);

        var mockCache = new Mock<ICacheService>();
        mockCache.Setup(c => c.GetThumbnailAsync(It.IsAny<string>()))
            .ReturnsAsync((byte[]?)null);

        var mockFileAccess = new Mock<IFileAccessService>();

        var cts = new CancellationTokenSource();
        // Cancel after cache check
        mockCache.Setup(c => c.GetThumbnailAsync(It.IsAny<string>()))
            .Callback(() => cts.Cancel())
            .ReturnsAsync((byte[]?)null);

        await imageInfo.Invoking(i => i.LoadThumbnailAsync(mockCache.Object, mockFileAccess.Object, ct: cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task LoadThumbnailAsync_HandlesException_Gracefully()
    {
        var entry = CreateFileEntry();
        var imageInfo = new ImageInfo(entry);

        var mockCache = new Mock<ICacheService>();
        mockCache.Setup(c => c.GetThumbnailAsync(It.IsAny<string>()))
            .ReturnsAsync((byte[]?)null);

        var mockFileAccess = new Mock<IFileAccessService>();
        mockFileAccess.Setup(f => f.ReadFileBytesAsync(It.IsAny<string>()))
            .ThrowsAsync(new IOException("File not found"));

        var mockOpenCv = new Mock<IOpenCvService>();

        // Should not throw, exception is caught and logged
        await imageInfo.LoadThumbnailAsync(mockCache.Object, mockFileAccess.Object, mockOpenCv.Object);

        // ThumbnailDataUrl should still be null (no fallback triggered since exception is logged)
        imageInfo.ThumbnailDataUrl.Should().BeNull();
    }

    [Fact]
    public async Task LoadThumbnailAsync_DoesNotCallOpenCV_WhenCacheHit()
    {
        var entry = CreateFileEntry();
        var imageInfo = new ImageInfo(entry);

        var cachedBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };

        var mockCache = new Mock<ICacheService>();
        mockCache.Setup(c => c.GetThumbnailAsync(imageInfo.Fingerprint))
            .ReturnsAsync(cachedBytes);

        var mockFileAccess = new Mock<IFileAccessService>();
        var mockOpenCv = new Mock<IOpenCvService>();

        await imageInfo.LoadThumbnailAsync(mockCache.Object, mockFileAccess.Object, mockOpenCv.Object);

        // Verify OpenCV was not called
        mockOpenCv.Verify(o => o.GenerateThumbnailAsync(It.IsAny<byte[]>(), It.IsAny<int>()), Times.Never);
    }

    #endregion
}
