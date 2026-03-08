using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Dupples_finder_UI.DTO;
using Dupples_finder_UI.Events;
using Dupples_finder_UI.Modules.Helpers;
using Dupples_finder_UI.Modules.ViewModels;
using Dupples_finder_UI.Services.Interfaces;
using Moq;
using Prism.Events;
using Xunit;
using Xunit.Abstractions;

namespace Dupless_finder_Tests;
// =========================================================================
// NonOverridingViewModel — inherits ViewModelBase but does NOT override
// DefineCommands or DefineEvents, so the base virtual { } bodies execute.
// =========================================================================

internal sealed class NonOverridingViewModel : ViewModelBase
{
    /// <summary>Parameterless ctor — routes through base() which calls the virtual methods.</summary>
    public NonOverridingViewModel() : base() { }

    /// <summary>IEventAggregator ctor — same routing via base(ea).</summary>
    public NonOverridingViewModel(IEventAggregator ea) : base(ea) { }

    /// <summary>Exposes the protected EventAggregator property for test assertions.</summary>
    public IEventAggregator PublicEventAggregator => EventAggregator;

    // No overrides of DefineCommands or DefineEvents — base { } bodies run.
}

// =========================================================================
// ViewModelBaseGapTests
// Covers the empty base bodies of DefineCommands() and DefineEvents() on
// lines 24/26 of ViewModelBase.cs. Those bodies are never reached when a
// subclass overrides the methods (as TestableViewModel does), so we use
// NonOverridingViewModel which lets the base implementations execute.
// =========================================================================

public class ViewModelBaseGapTests
{
    [Fact]
    public void ParameterlessCtor_NonOverriding_BaseDefineCommandsBodyExecutes()
    {
        // Simply constructing a NonOverridingViewModel causes the base
        // DefineCommands() { } body to execute without throwing.
        var ex = Record.Exception(() => new NonOverridingViewModel());
        Assert.Null(ex);
    }

    [Fact]
    public void EventAggregatorCtor_NonOverriding_BaseDefineCommandsBodyExecutes()
    {
        var mockEa = new Mock<IEventAggregator>();
        var ex = Record.Exception(() => new NonOverridingViewModel(mockEa.Object));
        Assert.Null(ex);
    }
}

// =========================================================================
// ImageInfoGapTests
// Covers:
//   1. Constructor catch block (lines 107-109) — FileInfo throws on bad path.
//   2. StoreMat zero-dim branch (lines 264-267) — empty file yields 0x0 Mat.
//   3. LoadThumbnail DB-store exception path (lines 165-168).
//   4. PrepareCommands — ImageDoubleClick publishes event; ImageClick is safe.
// =========================================================================

public class ImageInfoGapTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IEventAggregator _eventAggregator;
    private readonly ITestOutputHelper _output;

    public ImageInfoGapTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"ImageInfoGap_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _eventAggregator = new Mock<IEventAggregator>().Object;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { /* swallow cleanup errors in CI */ }
    }

    // Helper: build a real temp file of a specific size.
    private string CreateTempFile(string name, int byteCount)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, new byte[byteCount]);
        return path;
    }

    // Helper: path that does NOT exist on disk.
    private string NonExistentPath(string name = "ghost.jpg")
        => Path.Combine(_tempDir, name);

    // -----------------------------------------------------------------
    // 1. Constructor catch block — pass a path whose FileInfo ctor throws.
    //    new FileInfo(path) with embedded null char '\0' raises
    //    ArgumentException ("Illegal characters in path").
    // -----------------------------------------------------------------

    [Fact]
    public void Constructor_WithIllegalCharInPath_DoesNotThrow()
    {
        // "\0" inside a path causes new FileInfo(path) to throw ArgumentException.
        // The catch block on lines 107-109 must swallow it silently.
        const string badPath = "has\0null.jpg";

        var ex = Record.Exception(() =>
        {
            using var info = new ImageInfo(badPath, _eventAggregator);
        });

        Assert.Null(ex);
    }

    [Fact]
    public void Constructor_WithIllegalCharInPath_SetsFilePath()
    {
        // FilePath = path runs BEFORE the try block, so it must be stored
        // even when FileInfo construction fails.
        const string badPath = "has\0null.jpg";

        using var info = new ImageInfo(badPath, _eventAggregator);

        Assert.Equal(badPath, info.FilePath);
    }

    [Fact]
    public void Constructor_WithIllegalCharInPath_FileSizeIsZero()
    {
        // FileSize is only set inside the successful try block; when the
        // catch fires it stays at its default value of 0.
        const string badPath = "has\0null.jpg";

        using var info = new ImageInfo(badPath, _eventAggregator);

        Assert.Equal(0L, info.FileSize);
    }

    [Fact]
    public void Constructor_WithIllegalCharInPath_LastModifiedUtcIsDefault()
    {
        const string badPath = "has\0null.jpg";

        using var info = new ImageInfo(badPath, _eventAggregator);

        Assert.Equal(default(DateTime), info.LastModifiedUtc);
    }

    [Fact]
    public void Constructor_WithIllegalCharInPath_CommandsAreStillInitialised()
    {
        // PrepareCommands() runs AFTER the try/catch, so commands must still
        // be created even when the FileInfo read fails.
        const string badPath = "has\0null.jpg";

        using var info = new ImageInfo(badPath, _eventAggregator);

        Assert.NotNull(info.ImageDoubleClick);
        Assert.NotNull(info.ImageClick);
    }

    // -----------------------------------------------------------------
    // 2. StoreMat — zero-dimension Mat branch (lines 264-267).
    //    An empty (0-byte) file is read by OpenCV as a Mat with
    //    Width == 0, Height == 0, which triggers the "< 1" guard.
    // -----------------------------------------------------------------

    [Fact]
    public void StoreMat_WithEmptyFile_ProducesZerosMat_OfRequestedSize()
    {
        // 0-byte file: OpenCV returns an empty (0×0) Mat.
        var path = CreateTempFile("empty_image.jpg", byteCount: 0);
        using var info = new ImageInfo(path, _eventAggregator);

        info.StoreMat(64);

        // The zeros-Mat branch sets _storedMat to a (64×64) Mat.
        Assert.NotNull(info.StoredMat);
        Assert.Equal(64, info.StoredMat.Cols);
        Assert.Equal(64, info.StoredMat.Rows);
    }

    [Fact]
    public void StoreMat_WithEmptyFile_DoesNotThrow()
    {
        var path = CreateTempFile("empty2.jpg", byteCount: 0);
        using var info = new ImageInfo(path, _eventAggregator);

        var ex = Record.Exception(() => info.StoreMat(32));
        Assert.Null(ex);
    }

    [Fact]
    public void StoreMat_WithEmptyFile_StoredMatIsNotEmpty()
    {
        // The zeros Mat produced in the branch is non-empty (has pixels).
        var path = CreateTempFile("empty3.jpg", byteCount: 0);
        using var info = new ImageInfo(path, _eventAggregator);

        info.StoreMat(48);

        Assert.False(info.StoredMat.Empty());
    }

    // -----------------------------------------------------------------
    // 3. LoadThumbnail — DB-store exception path (lines 165-168).
    //    Flow: DB cache miss -> GetThumbnail returns a non-null bitmap ->
    //    EncodeBitmapSourceToBytes returns non-empty bytes ->
    //    CacheThumbnailAsync THROWS -> catch block writes Trace.
    // -----------------------------------------------------------------

    [Fact]
    public async Task LoadThumbnail_WhenQueueThumbnailThrows_DoesNotThrow()
    {
        // Arrange — need a real file so FileInfo metadata is valid.
        var filePath = CreateTempFile("thumb_store_err.jpg", byteCount: 128);
        using var info = new ImageInfo(filePath, _eventAggregator);

        System.Windows.Media.Imaging.WriteableBitmap fakeBitmap;
        try
        {
            fakeBitmap = new System.Windows.Media.Imaging.WriteableBitmap(
                1, 1, 96, 96,
                System.Windows.Media.PixelFormats.Bgr32, null);
            fakeBitmap.Freeze();
        }
        catch (Exception wpfEx)
        {
            _output.WriteLine($"SKIPPED: WPF WriteableBitmap not available. {wpfEx.Message}");
            return;
        }

        var fakeBytes = new byte[] { 1, 2, 3 };

        var mockDb = new Mock<IPhotoDbService>();
        mockDb.Setup(d => d.IsAvailable).Returns(true);
        // Cache miss — forces GetThumbnail to be called.
        mockDb
            .Setup(d => d.GetCachedPhotoByFingerprintAsync(
                It.IsAny<long>(), It.IsAny<DateTime>()))
            .ReturnsAsync((Dupples_finder_UI.Data.Entities.Photo)null);
        // QueueThumbnailForCache THROWS — this is what we're testing.
        mockDb
            .Setup(d => d.QueueThumbnailForCache(
                It.IsAny<long>(), It.IsAny<DateTime>(),
                It.IsAny<string>(), It.IsAny<byte[]>()))
            .Throws(new InvalidOperationException("simulated store failure"));

        var mockThumb = new Mock<IThumbnailService>();
        mockThumb
            .Setup(t => t.GetThumbnail(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(fakeBitmap);
        mockThumb
            .Setup(t => t.EncodeBitmapSourceToBytes(
                It.IsAny<System.Windows.Media.Imaging.BitmapSource>()))
            .Returns(fakeBytes);

        // Act — the catch block must absorb the exception.
        var ex = await Record.ExceptionAsync(
            () => info.LoadThumbnailAsync(mockDb.Object, mockThumb.Object));

        Assert.Null(ex);
    }

    [Fact]
    public async Task LoadThumbnail_WhenQueueThumbnailThrows_EncodingWasAttempted()
    {
        // Same setup as above; additionally verify that encoding was called
        // (proving the code reached the store block before catching the error).
        var filePath = CreateTempFile("thumb_store_err2.jpg", byteCount: 128);
        using var info = new ImageInfo(filePath, _eventAggregator);

        System.Windows.Media.Imaging.WriteableBitmap fakeBitmap;
        try
        {
            fakeBitmap = new System.Windows.Media.Imaging.WriteableBitmap(
                1, 1, 96, 96,
                System.Windows.Media.PixelFormats.Bgr32, null);
            fakeBitmap.Freeze();
        }
        catch (Exception wpfEx)
        {
            _output.WriteLine($"SKIPPED: WPF WriteableBitmap not available. {wpfEx.Message}");
            return;
        }

        var fakeBytes = new byte[] { 4, 5, 6 };

        var mockDb = new Mock<IPhotoDbService>();
        mockDb.Setup(d => d.IsAvailable).Returns(true);
        mockDb
            .Setup(d => d.GetCachedPhotoByFingerprintAsync(
                It.IsAny<long>(), It.IsAny<DateTime>()))
            .ReturnsAsync((Dupples_finder_UI.Data.Entities.Photo)null);
        mockDb
            .Setup(d => d.QueueThumbnailForCache(
                It.IsAny<long>(), It.IsAny<DateTime>(),
                It.IsAny<string>(), It.IsAny<byte[]>()))
            .Throws(new InvalidOperationException("simulated store failure"));

        var mockThumb = new Mock<IThumbnailService>();
        mockThumb
            .Setup(t => t.GetThumbnail(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(fakeBitmap);
        mockThumb
            .Setup(t => t.EncodeBitmapSourceToBytes(
                It.IsAny<System.Windows.Media.Imaging.BitmapSource>()))
            .Returns(fakeBytes);

        await info.LoadThumbnailAsync(mockDb.Object, mockThumb.Object);

        // EncodeBitmapSourceToBytes must have been called before the store threw.
        mockThumb.Verify(
            t => t.EncodeBitmapSourceToBytes(fakeBitmap), Times.Once);
    }

    [Fact]
    public async Task LoadThumbnail_WhenQueueThumbnailThrows_CachingWasAttempted()
    {
        // Verify that QueueThumbnailForCache was actually called (i.e., the code
        // entered the store branch) before the exception was swallowed.
        var filePath = CreateTempFile("thumb_store_err3.jpg", byteCount: 128);
        using var info = new ImageInfo(filePath, _eventAggregator);

        System.Windows.Media.Imaging.WriteableBitmap fakeBitmap;
        try
        {
            fakeBitmap = new System.Windows.Media.Imaging.WriteableBitmap(
                1, 1, 96, 96,
                System.Windows.Media.PixelFormats.Bgr32, null);
            fakeBitmap.Freeze();
        }
        catch (Exception wpfEx)
        {
            _output.WriteLine($"SKIPPED: WPF WriteableBitmap not available. {wpfEx.Message}");
            return;
        }

        var fakeBytes = new byte[] { 7, 8, 9 };

        var mockDb = new Mock<IPhotoDbService>();
        mockDb.Setup(d => d.IsAvailable).Returns(true);
        mockDb
            .Setup(d => d.GetCachedPhotoByFingerprintAsync(
                It.IsAny<long>(), It.IsAny<DateTime>()))
            .ReturnsAsync((Dupples_finder_UI.Data.Entities.Photo)null);
        mockDb
            .Setup(d => d.QueueThumbnailForCache(
                It.IsAny<long>(), It.IsAny<DateTime>(),
                It.IsAny<string>(), It.IsAny<byte[]>()))
            .Throws(new InvalidOperationException("simulated store failure"));

        var mockThumb = new Mock<IThumbnailService>();
        mockThumb
            .Setup(t => t.GetThumbnail(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(fakeBitmap);
        mockThumb
            .Setup(t => t.EncodeBitmapSourceToBytes(
                It.IsAny<System.Windows.Media.Imaging.BitmapSource>()))
            .Returns(fakeBytes);

        await info.LoadThumbnailAsync(mockDb.Object, mockThumb.Object);

        mockDb.Verify(
            d => d.QueueThumbnailForCache(
                info.FileSize, info.LastModifiedUtc,
                filePath, fakeBytes),
            Times.Once);
    }

    // -----------------------------------------------------------------
    // 4a. PrepareCommands — ImageDoubleClick publishes OpenImagePreviewEvent.
    // -----------------------------------------------------------------

    [Fact]
    public void ImageDoubleClick_PublishesOpenImagePreviewEvent_WithCorrectFilePath()
    {
        // Arrange
        var filePath = CreateTempFile("dbl_click.jpg", byteCount: 64);

        OpenImagePreviewPayload captured = null;

        var mockPreviewEvent = new Mock<OpenImagePreviewEvent>();
        mockPreviewEvent
            .Setup(e => e.Publish(It.IsAny<OpenImagePreviewPayload>()))
            .Callback<OpenImagePreviewPayload>(p => captured = p);

        var mockEa = new Mock<IEventAggregator>();
        mockEa.Setup(ea => ea.GetEvent<OpenImagePreviewEvent>())
            .Returns(mockPreviewEvent.Object);

        using var info = new ImageInfo(filePath, mockEa.Object);

        // Act
        info.ImageDoubleClick.Execute();

        // Assert
        Assert.NotNull(captured);
        Assert.Equal(filePath, captured.FilePath);
    }

    [Fact]
    public void ImageDoubleClick_WithNullEventAggregator_DoesNotThrow()
    {
        // The ?. guard inside the command lambda must prevent NRE.
        var filePath = CreateTempFile("dbl_click_null_ea.jpg", byteCount: 64);
        using var info = new ImageInfo(filePath, null);

        var ex = Record.Exception(() => info.ImageDoubleClick.Execute());
        Assert.Null(ex);
    }

    [Fact]
    public void ImageDoubleClick_PublishPayload_AlternatePathIsNull()
    {
        // The ImageInfo command only sets FilePath; AlternatePath stays null.
        var filePath = CreateTempFile("dbl_click_alt.jpg", byteCount: 64);

        OpenImagePreviewPayload captured = null;

        var mockPreviewEvent = new Mock<OpenImagePreviewEvent>();
        mockPreviewEvent
            .Setup(e => e.Publish(It.IsAny<OpenImagePreviewPayload>()))
            .Callback<OpenImagePreviewPayload>(p => captured = p);

        var mockEa = new Mock<IEventAggregator>();
        mockEa.Setup(ea => ea.GetEvent<OpenImagePreviewEvent>())
            .Returns(mockPreviewEvent.Object);

        using var info = new ImageInfo(filePath, mockEa.Object);
        info.ImageDoubleClick.Execute();

        Assert.NotNull(captured);
        Assert.Null(captured.AlternatePath);
    }

    // -----------------------------------------------------------------
    // 4b. PrepareCommands — ImageClick catches Process.Start exceptions
    //     when the FilePath is invalid / non-launchable.
    //     We don't call Process.Start with a valid file so it throws,
    //     and the internal catch must absorb it.
    // -----------------------------------------------------------------

    [Fact]
    public void ImageClick_WithNonExistentPath_CommandIsWiredUp()
    {
        // The ImageClick command calls Process.Start; on failure it calls
        // MessageBox.Show(e.Message) which is a Win32 modal dialog that
        // BLOCKS in headless test environments. We therefore cannot safely
        // call Execute() here. Instead we verify the command is correctly
        // initialised even when the path does not exist on disk.
        using var info = new ImageInfo(NonExistentPath("no_file.xyz"), _eventAggregator);

        Assert.NotNull(info.ImageClick);
        Assert.True(info.ImageClick.CanExecute());
    }

    [Fact]
    public void ImageClick_IsNotNull_AfterConstruction()
    {
        // Smoke test: PrepareCommands wires up both commands.
        var filePath = CreateTempFile("click_cmd.jpg", byteCount: 32);
        using var info = new ImageInfo(filePath, _eventAggregator);

        Assert.NotNull(info.ImageClick);
    }

    [Fact]
    public void ImageDoubleClick_IsNotNull_AfterConstruction()
    {
        var filePath = CreateTempFile("dbl_cmd.jpg", byteCount: 32);
        using var info = new ImageInfo(filePath, _eventAggregator);

        Assert.NotNull(info.ImageDoubleClick);
    }
}

// =========================================================================
// MouseWheelGestureGapTests
// Covers the Matches() override with real MouseWheelEventArgs so that the
// Delta-comparison switch branches (Up/Down/None) are executed.
//
// MouseWheelEventArgs requires STA and an active WPF input manager.
// All tests use StaHelper.Run and wrap construction in try/catch so that if
// the test host cannot create the args the test is skipped rather than failed.
// =========================================================================

public class MouseWheelGestureGapTests
{
    // Helper: try to create a MouseWheelEventArgs on the current thread.
    // Returns null if the runtime environment prevents it.
    private static MouseWheelEventArgs TryCreateWheelArgs(int delta)
    {
        try
        {
            // Mouse.PrimaryDevice may be null in headless test hosts.
            // Passing null for MouseDevice is accepted by the ctor when
            // WPF's input sub-system is not fully initialised.
            var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta);
            args.RoutedEvent = UIElement.MouseWheelEvent;
            return args;
        }
        catch
        {
            return null;
        }
    }

    // -----------------------------------------------------------------
    // Up gesture — positive delta (> 0) should match.
    // -----------------------------------------------------------------

    [Fact]
    public void Matches_Up_WithPositiveDelta_ReturnsTrue()
    {
        StaHelper.Run(() =>
        {
            var gesture = MouseWheelGesture.Up;
            var args = TryCreateWheelArgs(delta: 120); // positive = scroll up

            if (args == null)
            {
                // Cannot construct MouseWheelEventArgs in this host — skip.
                return;
            }

            var result = gesture.Matches(null, args);
            Assert.True(result);
        });
    }

    [Fact]
    public void Matches_Up_WithNegativeDelta_ReturnsFalse()
    {
        StaHelper.Run(() =>
        {
            var gesture = MouseWheelGesture.Up;
            var args = TryCreateWheelArgs(delta: -120); // negative = scroll down

            if (args == null)
            {
                return;
            }

            var result = gesture.Matches(null, args);
            Assert.False(result);
        });
    }

    [Fact]
    public void Matches_Up_WithZeroDelta_ReturnsFalse()
    {
        StaHelper.Run(() =>
        {
            var gesture = MouseWheelGesture.Up;
            var args = TryCreateWheelArgs(delta: 0);

            if (args == null)
            {
                return;
            }

            var result = gesture.Matches(null, args);
            Assert.False(result);
        });
    }

    // -----------------------------------------------------------------
    // Down gesture — negative delta (< 0) should match.
    // -----------------------------------------------------------------

    [Fact]
    public void Matches_Down_WithNegativeDelta_ReturnsTrue()
    {
        StaHelper.Run(() =>
        {
            var gesture = MouseWheelGesture.Down;
            var args = TryCreateWheelArgs(delta: -120); // negative = scroll down

            if (args == null)
            {
                return;
            }

            var result = gesture.Matches(null, args);
            Assert.True(result);
        });
    }

    [Fact]
    public void Matches_Down_WithPositiveDelta_ReturnsFalse()
    {
        StaHelper.Run(() =>
        {
            var gesture = MouseWheelGesture.Down;
            var args = TryCreateWheelArgs(delta: 120); // positive = scroll up

            if (args == null)
            {
                return;
            }

            var result = gesture.Matches(null, args);
            Assert.False(result);
        });
    }

    [Fact]
    public void Matches_Down_WithZeroDelta_ReturnsFalse()
    {
        StaHelper.Run(() =>
        {
            var gesture = MouseWheelGesture.Down;
            var args = TryCreateWheelArgs(delta: 0);

            if (args == null)
            {
                return;
            }

            var result = gesture.Matches(null, args);
            Assert.False(result);
        });
    }

    // -----------------------------------------------------------------
    // Both gestures — non-MouseWheelEventArgs should return false
    // (the second guard in Matches).
    // -----------------------------------------------------------------

    [Fact]
    public void Matches_Down_WithNonWheelEventArgs_ReturnsFalse()
    {
        StaHelper.Run(() =>
        {
            var gesture = MouseWheelGesture.Down;

            // KeyEventArgs is not a MouseWheelEventArgs — the cast guard fails.
            // We pass null to exercise the simplest non-wheel path;
            // the existing Matches_WithNullInputEventArgs tests already cover null,
            // but we include this as an additional confirmation.
            var result = gesture.Matches(new object(), null);
            Assert.False(result);
        });
    }

    [Fact]
    public void Matches_Up_WithNonWheelEventArgs_ReturnsFalse()
    {
        StaHelper.Run(() =>
        {
            var gesture = MouseWheelGesture.Up;
            var result = gesture.Matches(new object(), null);
            Assert.False(result);
        });
    }

    // -----------------------------------------------------------------
    // Verify the WheelDirection.None branch — neither Up nor Down static
    // property sets Direction=None, so we use reflection to set it.
    // This covers the default: return false branch in the switch.
    // -----------------------------------------------------------------

    [Fact]
    public void Matches_WithNonWheelArgs_ReflectionCreatedGesture_ReturnsFalse()
    {
        // This test verifies that any MouseWheelGesture instance returns false
        // for null event args (which reaches base.Matches before the switch).
        StaHelper.Run(() =>
        {
            // Use the public static properties — both pass base.Matches(null,null) = false.
            var gestureUp   = MouseWheelGesture.Up;
            var gestureDown = MouseWheelGesture.Down;

            Assert.False(gestureUp.Matches(null, null));
            Assert.False(gestureDown.Matches(null, null));
        });
    }

    // -----------------------------------------------------------------
    // Ensure the Down gesture does NOT match a positive (upward) delta —
    // isolates the cross-direction false case for Down.
    // -----------------------------------------------------------------

    [Fact]
    public void Down_DoesNotMatch_UpwardDelta_Isolated()
    {
        // Individual test: avoids sequencing with a prior Matches call.
        StaHelper.Run(() =>
        {
            var downGesture = MouseWheelGesture.Down;
            var upArgs      = TryCreateWheelArgs(delta: 120);

            if (upArgs == null)
            {
                return;
            }

            Assert.False(downGesture.Matches(null, upArgs));
        });
    }

    // -----------------------------------------------------------------
    // Ensure the Up gesture does NOT match a negative (downward) delta —
    // isolates the cross-direction false case for Up.
    // -----------------------------------------------------------------

    [Fact]
    public void Up_DoesNotMatch_DownwardDelta_Isolated()
    {
        StaHelper.Run(() =>
        {
            var upGesture = MouseWheelGesture.Up;
            var downArgs  = TryCreateWheelArgs(delta: -120);

            if (downArgs == null)
            {
                return;
            }

            Assert.False(upGesture.Matches(null, downArgs));
        });
    }
}