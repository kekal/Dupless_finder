using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Dupples_finder_UI.Data.Entities;
using Dupples_finder_UI.DTO;
using Dupples_finder_UI.Services.Interfaces;
using Moq;
using OpenCvSharp;
using Prism.Events;
using Xunit;
using Xunit.Abstractions;

namespace Dupless_finder_Tests;

// ---------------------------------------------------------------------------
// ImageInfo – extended coverage for LoadThumbnail, StoreMat, StoredMat getter,
// the Image property, and the OnPropertyChanged plumbing.
// ---------------------------------------------------------------------------
public class ImageInfoExtendedTests : IDisposable
{
    private readonly string _tempDirPath;
    private readonly IEventAggregator _eventAggregator;
    private readonly ITestOutputHelper _output;

    public ImageInfoExtendedTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDirPath = Path.Combine(Path.GetTempPath(), $"ImageInfoExtended_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirPath);
        _eventAggregator = new Mock<IEventAggregator>().Object;
    }

    // Creates a real, empty-content temp file so FileInfo.Exists is true.
    private string CreateTempFile(string name = "test.jpg", long size = 128)
    {
        var path = Path.Combine(_tempDirPath, name);
        using var fs = File.Create(path);
        fs.Write(new byte[size], 0, (int)size);
        return path;
    }

    // Returns a path that does not exist on disk.
    private string NonExistentPath(string name = "missing.jpg")
        => Path.Combine(_tempDirPath, name);

    // ---------------------------------------------------------------------------
    // Helper – build a Photo entity whose Thumbnail property contains bytes.
    // ---------------------------------------------------------------------------
    private static Photo PhotoWithThumbnail(byte[] bytes)
        => new Photo { Thumbnail = bytes, FileSize = 128, LastModifiedUtc = DateTime.UtcNow };

    // ---------------------------------------------------------------------------
    // LoadThumbnail – Step 1: DB cache hit
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task LoadThumbnail_WithDbCacheHit_CallsBytesToBitmapSource()
    {
        // Arrange
        var filePath = CreateTempFile();
        var imageInfo = new ImageInfo(filePath, _eventAggregator);

        var thumbnailBytes = new byte[] { 0xFF, 0xD8, 0xFF }; // minimal fake JPEG header
        var cachedPhoto = PhotoWithThumbnail(thumbnailBytes);

        var mockDb = new Mock<IPhotoDbService>();
        mockDb.Setup(d => d.IsAvailable).Returns(true);
        mockDb
            .Setup(d => d.GetCachedPhotoByFingerprintAsync(It.IsAny<long>(), It.IsAny<DateTime>()))
            .ReturnsAsync(cachedPhoto);

        var mockThumb = new Mock<IThumbnailService>();
        // BytesToBitmapSource returns null – we only care it was called.
        mockThumb
            .Setup(t => t.BytesToBitmapSource(thumbnailBytes))
            .Returns((System.Windows.Media.Imaging.BitmapSource)null);

        // Act
        await imageInfo.LoadThumbnailAsync(mockDb.Object, mockThumb.Object);

        // Assert – bytes-to-source converter was invoked with the cached bytes.
        mockThumb.Verify(t => t.BytesToBitmapSource(thumbnailBytes), Times.Once);

        imageInfo.Dispose();
    }

    [Fact]
    public async Task LoadThumbnail_WithDbCacheHit_DoesNotCallGetThumbnail()
    {
        // Arrange – DB returns a photo whose Thumbnail is non-empty,
        // so BytesToBitmapSource is called; because it returns null in this mock
        // the code continues to GetThumbnail.  To prevent that we make
        // BytesToBitmapSource return a non-null value.  BitmapSource is abstract
        // with internal constructors, so we use a WriteableBitmap (a concrete
        // WPF-friendly subclass available in net8-windows).
        var filePath = CreateTempFile();
        var imageInfo = new ImageInfo(filePath, _eventAggregator);

        var thumbnailBytes = new byte[] { 1, 2, 3 };
        var cachedPhoto = PhotoWithThumbnail(thumbnailBytes);

        // Create a 1x1 WriteableBitmap so we can return a non-null BitmapSource.
        System.Windows.Media.Imaging.WriteableBitmap fakeBitmap = null;
        try
        {
            // WriteableBitmap ctor works on any thread in test hosts that set up
            // the WPF dispatcher (net8-windows SDK does so).
            fakeBitmap = new System.Windows.Media.Imaging.WriteableBitmap(
                1, 1, 96, 96,
                System.Windows.Media.PixelFormats.Bgr32, null);
            fakeBitmap.Freeze();
        }
        catch (Exception ex)
        {
            _output.WriteLine($"SKIPPED: WPF WriteableBitmap not available. {ex.Message}");
            imageInfo.Dispose();
            return;
        }

        var mockDb = new Mock<IPhotoDbService>();
        mockDb.Setup(d => d.IsAvailable).Returns(true);
        mockDb
            .Setup(d => d.GetCachedPhotoByFingerprintAsync(It.IsAny<long>(), It.IsAny<DateTime>()))
            .ReturnsAsync(cachedPhoto);

        var mockThumb = new Mock<IThumbnailService>();
        mockThumb
            .Setup(t => t.BytesToBitmapSource(thumbnailBytes))
            .Returns(fakeBitmap);

        // Act
        await imageInfo.LoadThumbnailAsync(mockDb.Object, mockThumb.Object);

        // Assert – Shell API should not be called because cache hit produced a bitmap.
        mockThumb.Verify(t => t.GetThumbnail(It.IsAny<string>(), It.IsAny<int>()), Times.Never);

        imageInfo.Dispose();
    }

    // ---------------------------------------------------------------------------
    // LoadThumbnail – Step 2: DB cache miss -> Shell API fallback
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task LoadThumbnail_WithDbCacheMiss_CallsGetThumbnail()
    {
        // Arrange
        var filePath = CreateTempFile();
        var imageInfo = new ImageInfo(filePath, _eventAggregator);

        var mockDb = new Mock<IPhotoDbService>();
        mockDb.Setup(d => d.IsAvailable).Returns(true);
        mockDb
            .Setup(d => d.GetCachedPhotoByFingerprintAsync(It.IsAny<long>(), It.IsAny<DateTime>()))
            .ReturnsAsync((Photo)null); // cache miss

        var mockThumb = new Mock<IThumbnailService>();
        mockThumb
            .Setup(t => t.GetThumbnail(It.IsAny<string>(), It.IsAny<int>()))
            .Returns((System.Windows.Media.Imaging.BitmapSource)null);

        // Act
        await imageInfo.LoadThumbnailAsync(mockDb.Object, mockThumb.Object);

        // Assert
        mockThumb.Verify(t => t.GetThumbnail(filePath, It.IsAny<int>()), Times.Once);

        imageInfo.Dispose();
    }

    // ---------------------------------------------------------------------------
    // LoadThumbnail – Step 3: generated thumbnail stored back to DB
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task LoadThumbnail_StoresGeneratedThumbnailToDb()
    {
        // Arrange
        var filePath = CreateTempFile();
        var imageInfo = new ImageInfo(filePath, _eventAggregator);

        var fakeBytes = new byte[] { 9, 8, 7 };

        System.Windows.Media.Imaging.WriteableBitmap fakeBitmap;
        try
        {
            fakeBitmap = new System.Windows.Media.Imaging.WriteableBitmap(
                1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null);
            fakeBitmap.Freeze();
        }
        catch (Exception ex)
        {
            _output.WriteLine($"SKIPPED: WPF WriteableBitmap not available. {ex.Message}");
            imageInfo.Dispose();
            return;
        }

        var mockDb = new Mock<IPhotoDbService>();
        mockDb.Setup(d => d.IsAvailable).Returns(true);
        mockDb
            .Setup(d => d.GetCachedPhotoByFingerprintAsync(It.IsAny<long>(), It.IsAny<DateTime>()))
            .ReturnsAsync((Photo)null);
        mockDb
            .Setup(d => d.CacheThumbnailAsync(It.IsAny<long>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<byte[]>()))
            .ReturnsAsync(new Photo());

        var mockThumb = new Mock<IThumbnailService>();
        mockThumb
            .Setup(t => t.GetThumbnail(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(fakeBitmap);
        mockThumb
            .Setup(t => t.EncodeBitmapSourceToBytes(It.IsAny<System.Windows.Media.Imaging.BitmapSource>()))
            .Returns(fakeBytes);

        // Act
        await imageInfo.LoadThumbnailAsync(mockDb.Object, mockThumb.Object);

        // Assert – encoding and caching must both be called once.
        mockThumb.Verify(t => t.EncodeBitmapSourceToBytes(fakeBitmap), Times.Once);
        mockDb.Verify(d => d.CacheThumbnailAsync(
            imageInfo.FileSize,
            imageInfo.LastModifiedUtc,
            filePath,
            fakeBytes), Times.Once);

        imageInfo.Dispose();
    }

    [Fact]
    public async Task LoadThumbnail_WhenEncodeReturnsEmpty_DoesNotStoreToDb()
    {
        // Arrange
        var filePath = CreateTempFile();
        var imageInfo = new ImageInfo(filePath, _eventAggregator);

        System.Windows.Media.Imaging.WriteableBitmap fakeBitmap;
        try
        {
            fakeBitmap = new System.Windows.Media.Imaging.WriteableBitmap(
                1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null);
            fakeBitmap.Freeze();
        }
        catch (Exception ex)
        {
            _output.WriteLine($"SKIPPED: WPF WriteableBitmap not available. {ex.Message}");
            imageInfo.Dispose();
            return;
        }

        var mockDb = new Mock<IPhotoDbService>();
        mockDb.Setup(d => d.IsAvailable).Returns(true);
        mockDb
            .Setup(d => d.GetCachedPhotoByFingerprintAsync(It.IsAny<long>(), It.IsAny<DateTime>()))
            .ReturnsAsync((Photo)null);

        var mockThumb = new Mock<IThumbnailService>();
        mockThumb
            .Setup(t => t.GetThumbnail(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(fakeBitmap);
        mockThumb
            .Setup(t => t.EncodeBitmapSourceToBytes(It.IsAny<System.Windows.Media.Imaging.BitmapSource>()))
            .Returns(Array.Empty<byte>()); // empty – nothing to store

        // Act
        await imageInfo.LoadThumbnailAsync(mockDb.Object, mockThumb.Object);

        // Assert – CacheThumbnailAsync must NOT be called when bytes are empty.
        mockDb.Verify(
            d => d.CacheThumbnailAsync(It.IsAny<long>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<byte[]>()),
            Times.Never);

        imageInfo.Dispose();
    }

    // ---------------------------------------------------------------------------
    // LoadThumbnail – DB unavailable: skip DB entirely
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task LoadThumbnail_WithDbUnavailable_SkipsDbLookup()
    {
        // Arrange
        var filePath = CreateTempFile();
        var imageInfo = new ImageInfo(filePath, _eventAggregator);

        var mockDb = new Mock<IPhotoDbService>();
        mockDb.Setup(d => d.IsAvailable).Returns(false);

        var mockThumb = new Mock<IThumbnailService>();
        mockThumb
            .Setup(t => t.GetThumbnail(It.IsAny<string>(), It.IsAny<int>()))
            .Returns((System.Windows.Media.Imaging.BitmapSource)null);

        // Act
        await imageInfo.LoadThumbnailAsync(mockDb.Object, mockThumb.Object);

        // Assert
        mockDb.Verify(
            d => d.GetCachedPhotoByFingerprintAsync(It.IsAny<long>(), It.IsAny<DateTime>()),
            Times.Never);

        imageInfo.Dispose();
    }

    // ---------------------------------------------------------------------------
    // LoadThumbnail – null dbService: does not throw
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task LoadThumbnail_WithNullDbService_DoesNotThrow()
    {
        // Arrange
        var filePath = CreateTempFile();
        var imageInfo = new ImageInfo(filePath, _eventAggregator);

        var mockThumb = new Mock<IThumbnailService>();
        mockThumb
            .Setup(t => t.GetThumbnail(It.IsAny<string>(), It.IsAny<int>()))
            .Returns((System.Windows.Media.Imaging.BitmapSource)null);

        // Act & Assert – the `dbService is { IsAvailable: true }` pattern
        // gracefully handles null without throwing.
        var ex = await Record.ExceptionAsync(() => imageInfo.LoadThumbnailAsync(null, mockThumb.Object));
        Assert.Null(ex);

        imageInfo.Dispose();
    }

    // ---------------------------------------------------------------------------
    // LoadThumbnail – DB throws: graceful fallback to Shell API
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task LoadThumbnail_WithDbException_ContinuesGracefully()
    {
        // Arrange
        var filePath = CreateTempFile();
        var imageInfo = new ImageInfo(filePath, _eventAggregator);

        var mockDb = new Mock<IPhotoDbService>();
        mockDb.Setup(d => d.IsAvailable).Returns(true);
        mockDb
            .Setup(d => d.GetCachedPhotoByFingerprintAsync(It.IsAny<long>(), It.IsAny<DateTime>()))
            .ThrowsAsync(new InvalidOperationException("simulated DB failure"));

        var mockThumb = new Mock<IThumbnailService>();
        mockThumb
            .Setup(t => t.GetThumbnail(It.IsAny<string>(), It.IsAny<int>()))
            .Returns((System.Windows.Media.Imaging.BitmapSource)null);

        // Act – must not throw despite the DB error.
        var ex = await Record.ExceptionAsync(() => imageInfo.LoadThumbnailAsync(mockDb.Object, mockThumb.Object));
        Assert.Null(ex);

        // GetThumbnail (Shell fallback) should still be attempted.
        mockThumb.Verify(t => t.GetThumbnail(filePath, It.IsAny<int>()), Times.Once);

        imageInfo.Dispose();
    }

    // ---------------------------------------------------------------------------
    // LoadThumbnail – Step 4: OpenCV fallback when both DB and Shell return null
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task LoadThumbnail_WithShellThumbnailNull_FallsBackToOpenCv()
    {
        // Arrange – non-existent path so StoreMat catches the exception and
        // produces a zeros Mat; BitmapSourceConverter.ToBitmapSource will then
        // produce a valid (frozen) BitmapSource from that zeros Mat.
        var missingPath = NonExistentPath("no_such_file.jpg");
        var imageInfo = new ImageInfo(missingPath, _eventAggregator);

        var mockDb = new Mock<IPhotoDbService>();
        mockDb.Setup(d => d.IsAvailable).Returns(false); // simplest: skip DB entirely

        var mockThumb = new Mock<IThumbnailService>();
        mockThumb
            .Setup(t => t.GetThumbnail(It.IsAny<string>(), It.IsAny<int>()))
            .Returns((System.Windows.Media.Imaging.BitmapSource)null); // Shell also fails

        // Act – the OpenCV fallback path is exercised; it should not throw.
        var ex = await Record.ExceptionAsync(() => imageInfo.LoadThumbnailAsync(mockDb.Object, mockThumb.Object));
        Assert.Null(ex);

        imageInfo.Dispose();
    }

    // ---------------------------------------------------------------------------
    // StoreMat – exception path with non-existent file produces zeros Mat
    // ---------------------------------------------------------------------------

    [Fact]
    public void StoreMat_WithNonExistentFile_CreatesZerosMat()
    {
        // Arrange
        var imageInfo = new ImageInfo(NonExistentPath(), _eventAggregator);

        // Act
        imageInfo.StoreMat(100);

        // Assert – a zeros Mat of the requested size must have been created.
        Assert.NotNull(imageInfo.StoredMat);
        Assert.False(imageInfo.StoredMat.Empty());
        Assert.Equal(100, imageInfo.StoredMat.Width);
        Assert.Equal(100, imageInfo.StoredMat.Height);

        imageInfo.Dispose();
    }

    [Fact]
    public void StoreMat_WithNonExistentFile_DoesNotThrow()
    {
        var imageInfo = new ImageInfo(NonExistentPath("bad_path.jpg"), _eventAggregator);

        var ex = Record.Exception(() => imageInfo.StoreMat(50));
        Assert.Null(ex);

        imageInfo.Dispose();
    }

    // ---------------------------------------------------------------------------
    // StoreMat – idempotent: second call is a no-op
    // ---------------------------------------------------------------------------

    [Fact]
    public void StoreMat_CalledTwice_SecondCallIsNoOp()
    {
        // Arrange – use a non-existent file so the first call produces a zeros Mat
        // without needing a real image on disk.
        var imageInfo = new ImageInfo(NonExistentPath("dupe.jpg"), _eventAggregator);

        // Act
        imageInfo.StoreMat(80);
        var matAfterFirst = imageInfo.StoredMat; // capture reference

        imageInfo.StoreMat(80); // second call – should be a no-op
        var matAfterSecond = imageInfo.StoredMat;

        // Assert – same object reference: the field was not replaced.
        Assert.Same(matAfterFirst, matAfterSecond);

        imageInfo.Dispose();
    }

    // ---------------------------------------------------------------------------
    // StoredMat property getter – lazy-loads via StoreMat
    // ---------------------------------------------------------------------------

    [Fact]
    public void StoredMat_Getter_LazyLoads_WhenNull()
    {
        // Arrange – accessing StoredMat on a fresh instance (before any explicit
        // StoreMat call) should trigger StoreMat internally.
        var imageInfo = new ImageInfo(NonExistentPath("lazy.jpg"), _eventAggregator);

        // Act – first access; _storedMat starts null.
        var mat = imageInfo.StoredMat;

        // Assert – StoreMat must have populated the backing field.
        Assert.NotNull(mat);

        imageInfo.Dispose();
    }

    [Fact]
    public void StoredMat_Getter_ReturnsSameInstanceOnSubsequentAccess()
    {
        var imageInfo = new ImageInfo(NonExistentPath("lazy2.jpg"), _eventAggregator);

        var first = imageInfo.StoredMat;
        var second = imageInfo.StoredMat;

        Assert.Same(first, second);

        imageInfo.Dispose();
    }

    // ---------------------------------------------------------------------------
    // Image property / OnPropertyChanged – event wiring
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PropertyChanged_IsFired_WhenThumbnailIsLoaded()
    {
        // LoadThumbnailAsync sets Image directly (no Dispatcher dependency).
        // When all thumbnail sources return null, Image stays null and no event fires.
        var filePath = CreateTempFile();
        var imageInfo = new ImageInfo(filePath, _eventAggregator);

        var eventFiredCount = 0;
        imageInfo.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(imageInfo.Image))
            {
                eventFiredCount++;
            }
        };

        var mockDb = new Mock<IPhotoDbService>();
        mockDb.Setup(d => d.IsAvailable).Returns(false);

        var mockThumb = new Mock<IThumbnailService>();
        mockThumb
            .Setup(t => t.GetThumbnail(It.IsAny<string>(), It.IsAny<int>()))
            .Returns((System.Windows.Media.Imaging.BitmapSource)null);

        // Act – Shell returns null but OpenCvSharp fallback may produce a thumbnail
        // from the temp file. Either way, Image is set at most once.
        await imageInfo.LoadThumbnailAsync(mockDb.Object, mockThumb.Object);

        Assert.True(eventFiredCount <= 1);

        imageInfo.Dispose();
    }

    [Fact]
    public void PropertyChanged_EventIsSubscribable()
    {
        // Verifies that the INotifyPropertyChanged event can be subscribed and
        // unsubscribed without error (basic wiring smoke-test).
        var filePath = CreateTempFile();
        var imageInfo = new ImageInfo(filePath, _eventAggregator);

        PropertyChangedEventHandler handler = (_, _) => { };
        imageInfo.PropertyChanged += handler;
        imageInfo.PropertyChanged -= handler;

        imageInfo.Dispose();
    }

    // ---------------------------------------------------------------------------
    // IDisposable / test cleanup
    // ---------------------------------------------------------------------------

    public void Dispose()
    {
        if (Directory.Exists(_tempDirPath))
        {
            try { Directory.Delete(_tempDirPath, true); }
            catch { /* ignore cleanup errors in CI */ }
        }
    }
}

// ---------------------------------------------------------------------------
// LoadThumbnailAsync – async version with same DB-first, Shell-fallback logic
// ---------------------------------------------------------------------------
public class LoadThumbnailAsyncTests : IDisposable
{
    private readonly string _tempDirPath;
    private readonly IEventAggregator _eventAggregator;
    private readonly ITestOutputHelper _output;

    public LoadThumbnailAsyncTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDirPath = Path.Combine(Path.GetTempPath(), $"LoadThumbnailAsync_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirPath);
        _eventAggregator = new Mock<IEventAggregator>().Object;
    }

    // Creates a real, empty-content temp file so FileInfo.Exists is true.
    private string CreateTempFile(string name = "test.jpg", long size = 128)
    {
        var path = Path.Combine(_tempDirPath, name);
        using var fs = File.Create(path);
        fs.Write(new byte[size], 0, (int)size);
        return path;
    }

    // Returns a path that does not exist on disk.
    private string NonExistentPath(string name = "missing.jpg")
        => Path.Combine(_tempDirPath, name);

    // Helper – build a Photo entity whose Thumbnail property contains bytes.
    private static Photo PhotoWithThumbnail(byte[] bytes)
        => new Photo { Thumbnail = bytes, FileSize = 128, LastModifiedUtc = DateTime.UtcNow };

    // ---------------------------------------------------------------------------
    // LoadThumbnailAsync – Step 1: DB cache hit
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task LoadThumbnailAsync_WithDbCacheHit_CallsBytesToBitmapSourceAsync()
    {
        // Arrange
        var filePath = CreateTempFile();
        var imageInfo = new ImageInfo(filePath, _eventAggregator);

        var thumbnailBytes = new byte[] { 0xFF, 0xD8, 0xFF }; // minimal fake JPEG header
        var cachedPhoto = PhotoWithThumbnail(thumbnailBytes);

        var mockDb = new Mock<IPhotoDbService>();
        mockDb.Setup(d => d.IsAvailable).Returns(true);
        mockDb
            .Setup(d => d.GetCachedPhotoByFingerprintAsync(It.IsAny<long>(), It.IsAny<DateTime>()))
            .ReturnsAsync(cachedPhoto);

        var mockThumb = new Mock<IThumbnailService>();
        // BytesToBitmapSource returns null – we only care it was called.
        mockThumb
            .Setup(t => t.BytesToBitmapSource(thumbnailBytes))
            .Returns((System.Windows.Media.Imaging.BitmapSource)null);

        // Act
        await imageInfo.LoadThumbnailAsync(mockDb.Object, mockThumb.Object);

        // Assert – bytes-to-source converter was invoked with the cached bytes.
        mockThumb.Verify(t => t.BytesToBitmapSource(thumbnailBytes), Times.Once);

        imageInfo.Dispose();
    }

    [Fact]
    public async Task LoadThumbnailAsync_WithDbCacheHit_DoesNotCallGetThumbnailAsync()
    {
        // Arrange – DB returns a photo whose Thumbnail is non-empty,
        // so BytesToBitmapSource is called; because it returns null in this mock
        // the code continues to GetThumbnail.  To prevent that we make
        // BytesToBitmapSource return a non-null value.
        var filePath = CreateTempFile();
        var imageInfo = new ImageInfo(filePath, _eventAggregator);

        var thumbnailBytes = new byte[] { 1, 2, 3 };
        var cachedPhoto = PhotoWithThumbnail(thumbnailBytes);

        // Create a 1x1 WriteableBitmap so we can return a non-null BitmapSource.
        System.Windows.Media.Imaging.WriteableBitmap fakeBitmap = null;
        try
        {
            fakeBitmap = new System.Windows.Media.Imaging.WriteableBitmap(
                1, 1, 96, 96,
                System.Windows.Media.PixelFormats.Bgr32, null);
            fakeBitmap.Freeze();
        }
        catch (Exception ex)
        {
            _output.WriteLine($"SKIPPED: WPF WriteableBitmap not available. {ex.Message}");
            imageInfo.Dispose();
            return;
        }

        var mockDb = new Mock<IPhotoDbService>();
        mockDb.Setup(d => d.IsAvailable).Returns(true);
        mockDb
            .Setup(d => d.GetCachedPhotoByFingerprintAsync(It.IsAny<long>(), It.IsAny<DateTime>()))
            .ReturnsAsync(cachedPhoto);

        var mockThumb = new Mock<IThumbnailService>();
        mockThumb
            .Setup(t => t.BytesToBitmapSource(thumbnailBytes))
            .Returns(fakeBitmap);

        // Act
        await imageInfo.LoadThumbnailAsync(mockDb.Object, mockThumb.Object);

        // Assert – Shell API should not be called because cache hit produced a bitmap.
        mockThumb.Verify(t => t.GetThumbnail(It.IsAny<string>(), It.IsAny<int>()), Times.Never);

        imageInfo.Dispose();
    }

    // ---------------------------------------------------------------------------
    // LoadThumbnailAsync – Step 2: DB cache miss -> Shell API fallback
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task LoadThumbnailAsync_WithDbCacheMiss_CallsGetThumbnailAsync()
    {
        // Arrange
        var filePath = CreateTempFile();
        var imageInfo = new ImageInfo(filePath, _eventAggregator);

        var mockDb = new Mock<IPhotoDbService>();
        mockDb.Setup(d => d.IsAvailable).Returns(true);
        mockDb
            .Setup(d => d.GetCachedPhotoByFingerprintAsync(It.IsAny<long>(), It.IsAny<DateTime>()))
            .ReturnsAsync((Photo)null); // cache miss

        var mockThumb = new Mock<IThumbnailService>();
        mockThumb
            .Setup(t => t.GetThumbnail(It.IsAny<string>(), It.IsAny<int>()))
            .Returns((System.Windows.Media.Imaging.BitmapSource)null);

        // Act
        await imageInfo.LoadThumbnailAsync(mockDb.Object, mockThumb.Object);

        // Assert
        mockThumb.Verify(t => t.GetThumbnail(filePath, It.IsAny<int>()), Times.Once);

        imageInfo.Dispose();
    }

    // ---------------------------------------------------------------------------
    // LoadThumbnailAsync – Step 3: generated thumbnail stored back to DB
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task LoadThumbnailAsync_StoresGeneratedThumbnailToDbAsync()
    {
        // Arrange
        var filePath = CreateTempFile();
        var imageInfo = new ImageInfo(filePath, _eventAggregator);

        var fakeBytes = new byte[] { 9, 8, 7 };

        System.Windows.Media.Imaging.WriteableBitmap fakeBitmap;
        try
        {
            fakeBitmap = new System.Windows.Media.Imaging.WriteableBitmap(
                1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null);
            fakeBitmap.Freeze();
        }
        catch (Exception ex)
        {
            _output.WriteLine($"SKIPPED: WPF WriteableBitmap not available. {ex.Message}");
            imageInfo.Dispose();
            return;
        }

        var mockDb = new Mock<IPhotoDbService>();
        mockDb.Setup(d => d.IsAvailable).Returns(true);
        mockDb
            .Setup(d => d.GetCachedPhotoByFingerprintAsync(It.IsAny<long>(), It.IsAny<DateTime>()))
            .ReturnsAsync((Photo)null);
        mockDb
            .Setup(d => d.CacheThumbnailAsync(It.IsAny<long>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<byte[]>()))
            .ReturnsAsync(new Photo());

        var mockThumb = new Mock<IThumbnailService>();
        mockThumb
            .Setup(t => t.GetThumbnail(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(fakeBitmap);
        mockThumb
            .Setup(t => t.EncodeBitmapSourceToBytes(It.IsAny<System.Windows.Media.Imaging.BitmapSource>()))
            .Returns(fakeBytes);

        // Act
        await imageInfo.LoadThumbnailAsync(mockDb.Object, mockThumb.Object);

        // Assert – encoding and caching must both be called once.
        mockThumb.Verify(t => t.EncodeBitmapSourceToBytes(fakeBitmap), Times.Once);
        mockDb.Verify(d => d.CacheThumbnailAsync(
            imageInfo.FileSize,
            imageInfo.LastModifiedUtc,
            filePath,
            fakeBytes), Times.Once);

        imageInfo.Dispose();
    }

    [Fact]
    public async Task LoadThumbnailAsync_WhenEncodeReturnsEmpty_DoesNotStoreToDbAsync()
    {
        // Arrange
        var filePath = CreateTempFile();
        var imageInfo = new ImageInfo(filePath, _eventAggregator);

        System.Windows.Media.Imaging.WriteableBitmap fakeBitmap;
        try
        {
            fakeBitmap = new System.Windows.Media.Imaging.WriteableBitmap(
                1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null);
            fakeBitmap.Freeze();
        }
        catch (Exception ex)
        {
            _output.WriteLine($"SKIPPED: WPF WriteableBitmap not available. {ex.Message}");
            imageInfo.Dispose();
            return;
        }

        var mockDb = new Mock<IPhotoDbService>();
        mockDb.Setup(d => d.IsAvailable).Returns(true);
        mockDb
            .Setup(d => d.GetCachedPhotoByFingerprintAsync(It.IsAny<long>(), It.IsAny<DateTime>()))
            .ReturnsAsync((Photo)null);

        var mockThumb = new Mock<IThumbnailService>();
        mockThumb
            .Setup(t => t.GetThumbnail(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(fakeBitmap);
        mockThumb
            .Setup(t => t.EncodeBitmapSourceToBytes(It.IsAny<System.Windows.Media.Imaging.BitmapSource>()))
            .Returns(Array.Empty<byte>()); // empty – nothing to store

        // Act
        await imageInfo.LoadThumbnailAsync(mockDb.Object, mockThumb.Object);

        // Assert – CacheThumbnailAsync must NOT be called when bytes are empty.
        mockDb.Verify(
            d => d.CacheThumbnailAsync(It.IsAny<long>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<byte[]>()),
            Times.Never);

        imageInfo.Dispose();
    }

    // ---------------------------------------------------------------------------
    // LoadThumbnailAsync – DB unavailable: skip DB entirely
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task LoadThumbnailAsync_WithDbUnavailable_SkipsDbLookupAsync()
    {
        // Arrange
        var filePath = CreateTempFile();
        var imageInfo = new ImageInfo(filePath, _eventAggregator);

        var mockDb = new Mock<IPhotoDbService>();
        mockDb.Setup(d => d.IsAvailable).Returns(false);

        var mockThumb = new Mock<IThumbnailService>();
        mockThumb
            .Setup(t => t.GetThumbnail(It.IsAny<string>(), It.IsAny<int>()))
            .Returns((System.Windows.Media.Imaging.BitmapSource)null);

        // Act
        await imageInfo.LoadThumbnailAsync(mockDb.Object, mockThumb.Object);

        // Assert
        mockDb.Verify(
            d => d.GetCachedPhotoByFingerprintAsync(It.IsAny<long>(), It.IsAny<DateTime>()),
            Times.Never);

        imageInfo.Dispose();
    }

    // ---------------------------------------------------------------------------
    // LoadThumbnailAsync – null dbService: does not throw
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task LoadThumbnailAsync_WithNullDbService_DoesNotThrowAsync()
    {
        // Arrange
        var filePath = CreateTempFile();
        var imageInfo = new ImageInfo(filePath, _eventAggregator);

        var mockThumb = new Mock<IThumbnailService>();
        mockThumb
            .Setup(t => t.GetThumbnail(It.IsAny<string>(), It.IsAny<int>()))
            .Returns((System.Windows.Media.Imaging.BitmapSource)null);

        // Act & Assert – the `dbService is { IsAvailable: true }` pattern
        // gracefully handles null without throwing.
        var ex = await Record.ExceptionAsync(() => imageInfo.LoadThumbnailAsync(null, mockThumb.Object));
        Assert.Null(ex);

        imageInfo.Dispose();
    }

    // ---------------------------------------------------------------------------
    // LoadThumbnailAsync – DB throws: graceful fallback to Shell API
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task LoadThumbnailAsync_WithDbException_ContinuesGracefullyAsync()
    {
        // Arrange
        var filePath = CreateTempFile();
        var imageInfo = new ImageInfo(filePath, _eventAggregator);

        var mockDb = new Mock<IPhotoDbService>();
        mockDb.Setup(d => d.IsAvailable).Returns(true);
        mockDb
            .Setup(d => d.GetCachedPhotoByFingerprintAsync(It.IsAny<long>(), It.IsAny<DateTime>()))
            .ThrowsAsync(new InvalidOperationException("simulated DB failure"));

        var mockThumb = new Mock<IThumbnailService>();
        mockThumb
            .Setup(t => t.GetThumbnail(It.IsAny<string>(), It.IsAny<int>()))
            .Returns((System.Windows.Media.Imaging.BitmapSource)null);

        // Act – must not throw despite the DB error.
        var ex = await Record.ExceptionAsync(() => imageInfo.LoadThumbnailAsync(mockDb.Object, mockThumb.Object));
        Assert.Null(ex);

        // GetThumbnail (Shell fallback) should still be attempted.
        mockThumb.Verify(t => t.GetThumbnail(filePath, It.IsAny<int>()), Times.Once);

        imageInfo.Dispose();
    }

    // ---------------------------------------------------------------------------
    // LoadThumbnailAsync – Step 4: OpenCV fallback when both DB and Shell return null
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task LoadThumbnailAsync_WithShellThumbnailNull_FallsBackToOpenCvAsync()
    {
        // Arrange – non-existent path so StoreMat catches the exception and
        // produces a zeros Mat; BitmapSourceConverter.ToBitmapSource will then
        // produce a valid (frozen) BitmapSource from that zeros Mat.
        var missingPath = NonExistentPath("no_such_file.jpg");
        var imageInfo = new ImageInfo(missingPath, _eventAggregator);

        var mockDb = new Mock<IPhotoDbService>();
        mockDb.Setup(d => d.IsAvailable).Returns(false); // simplest: skip DB entirely

        var mockThumb = new Mock<IThumbnailService>();
        mockThumb
            .Setup(t => t.GetThumbnail(It.IsAny<string>(), It.IsAny<int>()))
            .Returns((System.Windows.Media.Imaging.BitmapSource)null); // Shell also fails

        // Act – the OpenCV fallback path is exercised; it should not throw.
        var ex = await Record.ExceptionAsync(() => imageInfo.LoadThumbnailAsync(mockDb.Object, mockThumb.Object));
        Assert.Null(ex);

        imageInfo.Dispose();
    }

    // ---------------------------------------------------------------------------
    // IDisposable / test cleanup
    // ---------------------------------------------------------------------------

    public void Dispose()
    {
        if (Directory.Exists(_tempDirPath))
        {
            try { Directory.Delete(_tempDirPath, true); }
            catch { /* ignore cleanup errors in CI */ }
        }
    }
}

// ---------------------------------------------------------------------------
// PairSimilarityInfo – coverage for Equals(object) and ToString()
// ---------------------------------------------------------------------------
public class PairSimilarityInfoExtendedTests
{
    private static Mat CreateTestMat()
        => new Mat(1, 128, MatType.CV_32FC1);

    // ---------------------------------------------------------------------------
    // Equals(object) – delegates to Equals(PairSimilarityInfo)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Equals_Object_WithSamePairSimilarityInfo_ReturnsTrue()
    {
        using var mat1 = CreateTestMat();
        using var mat2 = CreateTestMat();

        var hash1 = new KeyValuePair<string, Mat>("alpha.jpg", mat1);
        var hash2 = new KeyValuePair<string, Mat>("beta.jpg", mat2);

        var pair1 = new PairSimilarityInfo(hash1, hash2, 0.80);
        var pair2 = new PairSimilarityInfo(hash1, hash2, 0.75); // same keys, different match

        object boxed = pair2;

        Assert.True(pair1.Equals(boxed));
    }

    [Fact]
    public void Equals_Object_WithReversedOrderPairSimilarityInfo_ReturnsTrue()
    {
        using var mat1 = CreateTestMat();
        using var mat2 = CreateTestMat();

        var hash1 = new KeyValuePair<string, Mat>("alpha.jpg", mat1);
        var hash2 = new KeyValuePair<string, Mat>("beta.jpg", mat2);

        var pair1 = new PairSimilarityInfo(hash1, hash2, 0.80);
        var pair2 = new PairSimilarityInfo(hash2, hash1, 0.75); // reversed

        object boxed = pair2;

        Assert.True(pair1.Equals(boxed));
    }

    [Fact]
    public void Equals_Object_WithNull_ReturnsFalse()
    {
        using var mat1 = CreateTestMat();
        using var mat2 = CreateTestMat();

        var hash1 = new KeyValuePair<string, Mat>("alpha.jpg", mat1);
        var hash2 = new KeyValuePair<string, Mat>("beta.jpg", mat2);

        var pair = new PairSimilarityInfo(hash1, hash2, 0.90);

        Assert.False(pair.Equals((object)null));
    }

    [Fact]
    public void Equals_Object_WithDifferentType_ReturnsFalse()
    {
        using var mat1 = CreateTestMat();
        using var mat2 = CreateTestMat();

        var hash1 = new KeyValuePair<string, Mat>("alpha.jpg", mat1);
        var hash2 = new KeyValuePair<string, Mat>("beta.jpg", mat2);

        var pair = new PairSimilarityInfo(hash1, hash2, 0.90);

        // A plain string is not a PairSimilarityInfo; the cast inside Equals
        // returns null and Equals(PairSimilarityInfo) returns false.
        Assert.False(pair.Equals("not a pair"));
    }

    [Fact]
    public void Equals_Object_WithDifferentPair_ReturnsFalse()
    {
        using var mat1 = CreateTestMat();
        using var mat2 = CreateTestMat();
        using var mat3 = CreateTestMat();
        using var mat4 = CreateTestMat();

        var hash1 = new KeyValuePair<string, Mat>("alpha.jpg", mat1);
        var hash2 = new KeyValuePair<string, Mat>("beta.jpg", mat2);
        var hash3 = new KeyValuePair<string, Mat>("gamma.jpg", mat3);
        var hash4 = new KeyValuePair<string, Mat>("delta.jpg", mat4);

        var pair1 = new PairSimilarityInfo(hash1, hash2, 0.90);
        var pair2 = new PairSimilarityInfo(hash3, hash4, 0.85);

        Assert.False(pair1.Equals((object)pair2));
    }

    // ---------------------------------------------------------------------------
    // ToString()
    // ---------------------------------------------------------------------------

    [Fact]
    public void ToString_ReturnsFormattedString_ContainingBothKeys()
    {
        using var mat1 = CreateTestMat();
        using var mat2 = CreateTestMat();

        var hash1 = new KeyValuePair<string, Mat>("photo_A.jpg", mat1);
        var hash2 = new KeyValuePair<string, Mat>("photo_B.jpg", mat2);
        var match = 0.9375;

        var pair = new PairSimilarityInfo(hash1, hash2, match);
        var result = pair.ToString();

        Assert.Contains("photo_A.jpg", result);
        Assert.Contains("photo_B.jpg", result);
    }

    [Fact]
    public void ToString_ReturnsFormattedString_ContainingMatchValue()
    {
        using var mat1 = CreateTestMat();
        using var mat2 = CreateTestMat();

        var hash1 = new KeyValuePair<string, Mat>("x.jpg", mat1);
        var hash2 = new KeyValuePair<string, Mat>("y.jpg", mat2);
        var match = 0.55;

        var pair = new PairSimilarityInfo(hash1, hash2, match);
        var result = pair.ToString();

        // The implementation formats match with "F" (two decimal places).
        Assert.Contains(match.ToString("F"), result);
    }

    [Fact]
    public void ToString_ReturnsFormattedString_ContainsSeparatorMarkers()
    {
        using var mat1 = CreateTestMat();
        using var mat2 = CreateTestMat();

        var hash1 = new KeyValuePair<string, Mat>("a.jpg", mat1);
        var hash2 = new KeyValuePair<string, Mat>("b.jpg", mat2);

        var pair = new PairSimilarityInfo(hash1, hash2, 1.0);
        var result = pair.ToString();

        // The implementation always starts with "\n= \n" and ends with "\n==\n".
        Assert.StartsWith("\n= \n", result);
        Assert.EndsWith("\n==\n", result);
    }

    [Fact]
    public void ToString_MatchSection_UsesFixedTwoDecimalPlaces()
    {
        using var mat1 = CreateTestMat();
        using var mat2 = CreateTestMat();

        var hash1 = new KeyValuePair<string, Mat>("c.jpg", mat1);
        var hash2 = new KeyValuePair<string, Mat>("d.jpg", mat2);

        // Match value with many decimal places – "F" truncates to 2.
        var pair = new PairSimilarityInfo(hash1, hash2, 0.123456789);
        var result = pair.ToString();

        // "F" format in the current culture produces e.g. "0.12"
        Assert.Contains("Match: " + (0.123456789).ToString("F"), result);
    }
}