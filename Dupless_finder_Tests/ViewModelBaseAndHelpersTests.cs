using System;
using System.Threading;
using Dupples_finder_UI.DTO;
using Dupples_finder_UI.Events;
using Dupples_finder_UI.Modules.Helpers;
using Dupples_finder_UI.Modules.ViewModels;
using Moq;
using Prism.Events;
using Xunit;

namespace Dupless_finder_Tests;
// =====================================================================
// STA thread helper
// =====================================================================

/// <summary>
/// Runs a test action on an STA thread, re-throwing any exception from
/// that thread back on the calling thread so xUnit can report it.
/// Required for types that derive from DependencyObject (e.g. ImagePair).
/// </summary>
internal static class StaHelper
{
    public static void Run(Action action)
    {
        Exception caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (caught != null)
        {
            throw caught;
        }
    }
}

// =====================================================================
// Concrete test double for ViewModelBase
// =====================================================================

/// <summary>
/// Minimal concrete subclass that lets us instantiate and inspect the
/// protected members of the abstract ViewModelBase.
/// </summary>
internal sealed class TestableViewModel : ViewModelBase
{
    public int DefineCommandsCallCount { get; private set; }
    public int DefineEventsCallCount   { get; private set; }
    public bool DisposeCalledWithTrue  { get; private set; }

    /// <summary>
    /// Exposes the protected EventAggregator property so external tests
    /// can assert on it without requiring InternalsVisibleTo.
    /// </summary>
    public IEventAggregator PublicEventAggregator => EventAggregator;

    // Parameterless constructor path
    public TestableViewModel() { }

    // IEventAggregator constructor path
    public TestableViewModel(IEventAggregator ea) : base(ea) { }

    protected override void DefineCommands()
    {
        DefineCommandsCallCount++;
    }

    protected override void DefineEvents()
    {
        DefineEventsCallCount++;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeCalledWithTrue = true;
        }
        base.Dispose(disposing);
    }
}

// =====================================================================
// Concrete test double for DisposableObject
// =====================================================================

/// <summary>
/// Concrete subclass of DisposableObject that counts how many times
/// Clean() is called, so we can assert single-call semantics.
/// </summary>
internal sealed class TrackingDisposable : Dupples_finder_UI.Modules.Helpers.DisposableObject
{
    public int CleanCallCount { get; private set; }

    protected override void Clean()
    {
        CleanCallCount++;
    }
}

// =====================================================================
// ViewModelBase Tests
// =====================================================================

public class ViewModelBaseTests
{
    [Fact]
    public void ParameterlessConstructor_CallsDefineCommandsAndDefineEvents()
    {
        // Act
        var vm = new TestableViewModel();

        // Assert -- each virtual hook must have been called exactly once
        Assert.Equal(1, vm.DefineCommandsCallCount);
        Assert.Equal(1, vm.DefineEventsCallCount);
    }

    [Fact]
    public void EventAggregatorConstructor_SetsEventAggregatorProperty()
    {
        // Arrange
        var mockEa = new Mock<IEventAggregator>();

        // Act
        var vm = new TestableViewModel(mockEa.Object);

        // Assert
        Assert.Same(mockEa.Object, vm.PublicEventAggregator);
    }

    [Fact]
    public void EventAggregatorConstructor_CallsDefineCommandsAndDefineEvents()
    {
        // Arrange
        var mockEa = new Mock<IEventAggregator>();

        // Act
        var vm = new TestableViewModel(mockEa.Object);

        // Assert
        Assert.Equal(1, vm.DefineCommandsCallCount);
        Assert.Equal(1, vm.DefineEventsCallCount);
    }

    [Fact]
    public void ParameterlessConstructor_EventAggregatorIsNull()
    {
        // When no IEventAggregator is supplied the property must stay null
        var vm = new TestableViewModel();

        Assert.Null(vm.PublicEventAggregator);
    }

    [Fact]
    public void Dispose_CallsProtectedDisposeTrueOnFirstCall()
    {
        // Arrange
        var vm = new TestableViewModel();

        // Act
        vm.Dispose();

        // Assert -- Dispose(true) must have been forwarded
        Assert.True(vm.DisposeCalledWithTrue);
    }

    [Fact]
    public void Dispose_IsIdempotent_SecondCallDoesNothing()
    {
        // Arrange
        var vm = new TestableViewModel();

        // Act -- call twice
        vm.Dispose();
        vm.Dispose();

        // Assert -- Dispose(bool) is still true (flag was set on first call)
        // and no exception was thrown on the second call
        Assert.True(vm.DisposeCalledWithTrue);
    }

    [Fact]
    public void Dispose_DoesNotThrow_WhenCalledMultipleTimes()
    {
        // Arrange
        var vm = new TestableViewModel();

        // Act & Assert -- no exception
        var ex = Record.Exception(() =>
        {
            vm.Dispose();
            vm.Dispose();
            vm.Dispose();
        });
        Assert.Null(ex);
    }

    [Fact]
    public void EventAggregatorConstructor_WithNullEventAggregator_DoesNotThrow()
    {
        // ViewModelBase allows null to be passed through the typed constructor
        var ex = Record.Exception(() => new TestableViewModel(null));
        Assert.Null(ex);
    }
}

// =====================================================================
// DisposableObject Tests
// =====================================================================

public class DisposableObjectTests
{
    [Fact]
    public void Dispose_CallsCleanExactlyOnce()
    {
        // Arrange
        var obj = new TrackingDisposable();

        // Act
        obj.Dispose();

        // Assert
        Assert.Equal(1, obj.CleanCallCount);
    }

    [Fact]
    public void Dispose_CalledTwice_CallsCleanOnlyOnce()
    {
        // Arrange
        var obj = new TrackingDisposable();

        // Act
        obj.Dispose();
        obj.Dispose();

        // Assert -- idempotent guard must prevent a second Clean() call
        Assert.Equal(1, obj.CleanCallCount);
    }

    [Fact]
    public void Dispose_CalledManyTimes_CleanCalledOnlyOnce()
    {
        // Arrange
        var obj = new TrackingDisposable();

        // Act
        for (var i = 0; i < 10; i++)
        {
            obj.Dispose();
        }

        // Assert
        Assert.Equal(1, obj.CleanCallCount);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        // Arrange
        var obj = new TrackingDisposable();

        // Act & Assert
        var ex = Record.Exception(() => obj.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void BaseClean_DoesNotThrow_WhenCalledDirectly()
    {
        // The base DisposableObject.Clean() is an empty virtual method;
        // instantiating the base class directly and disposing exercises it.
        var obj = new Dupples_finder_UI.Modules.Helpers.DisposableObject();

        // No exception should be thrown when disposing
        obj.Dispose();
    }

    [Fact]
    public void Finalize_ViaGc_DoesNotThrow()
    {
        // Create the object in a nested scope so it becomes eligible for GC
        // immediately after the scope exits, then force collection.
        // This indirectly exercises the ~DisposableObject() finalizer path.
        static void CreateAndAbandon()
        {
            // Intentionally not disposed -- we rely on the finalizer
            var _ = new TrackingDisposable();
        }

        CreateAndAbandon();

        // Force two GC cycles: first to detect unreachable objects,
        // second to run pending finalizers.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);

        // If we reach this line without an exception the finalizer path is safe.
    }
}

// =====================================================================
// ImagePair Tests  (STA thread required -- DependencyObject)
// =====================================================================

public class ImagePairTests
{
    private static IEventAggregator BuildMockEventAggregator()
    {
        var mockEa    = new Mock<IEventAggregator>();
        var mockEvent = new Mock<OpenImagePreviewEvent>();

        // Allow any payload to be published without side-effects
        mockEvent.Setup(e => e.Publish(It.IsAny<OpenImagePreviewPayload>()));

        mockEa.Setup(ea => ea.GetEvent<OpenImagePreviewEvent>())
            .Returns(mockEvent.Object);

        return mockEa.Object;
    }

    [Fact]
    public void Constructor_SetsThumbnailSizeDependencyProperty()
    {
        StaHelper.Run(() =>
        {
            // Arrange
            const ushort size = 128;
            var ea = BuildMockEventAggregator();

            // Act
            var pair = new ImagePair(size, ea);

            // Assert
            Assert.Equal(size, pair.ThumbnailSize);
        });
    }

    [Fact]
    public void Constructor_CreatesImage1DoubleClickCommand()
    {
        StaHelper.Run(() =>
        {
            var ea   = BuildMockEventAggregator();
            var pair = new ImagePair(200, ea);

            Assert.NotNull(pair.Image1DoubleClick);
        });
    }

    [Fact]
    public void Constructor_CreatesImage2DoubleClickCommand()
    {
        StaHelper.Run(() =>
        {
            var ea   = BuildMockEventAggregator();
            var pair = new ImagePair(200, ea);

            Assert.NotNull(pair.Image2DoubleClick);
        });
    }

    [Fact]
    public void BestDistance_ReturnsMatchFormattedToTwoDecimalPlaces()
    {
        StaHelper.Run(() =>
        {
            // Arrange
            var ea   = BuildMockEventAggregator();
            var pair = new ImagePair(100, ea) { Match = 3.14159 };

            // Act
            var result = pair.BestDistance;

            // Assert -- "F" format gives two decimal places
            Assert.Equal("3.14", result);
        });
    }

    [Fact]
    public void BestDistance_WhenMatchIsZero_ReturnsZeroFormatted()
    {
        StaHelper.Run(() =>
        {
            var ea   = BuildMockEventAggregator();
            var pair = new ImagePair(100, ea) { Match = 0.0 };

            Assert.Equal("0.00", pair.BestDistance);
        });
    }

    [Fact]
    public void Image1DoubleClick_PublishesEventWithImage1PathAsFilePath()
    {
        StaHelper.Run(() =>
        {
            // Arrange
            OpenImagePreviewPayload captured = null;

            var mockEvent = new Mock<OpenImagePreviewEvent>();
            mockEvent
                .Setup(e => e.Publish(It.IsAny<OpenImagePreviewPayload>()))
                .Callback<OpenImagePreviewPayload>(p => captured = p);

            var mockEa = new Mock<IEventAggregator>();
            mockEa.Setup(ea => ea.GetEvent<OpenImagePreviewEvent>())
                .Returns(mockEvent.Object);

            var pair = new ImagePair(100, mockEa.Object)
            {
                Image1 = new ImageInfo("C:/img1.jpg", null),
                Image2 = new ImageInfo("C:/img2.jpg", null)
            };

            // Act
            pair.Image1DoubleClick.Execute();

            // Assert
            Assert.NotNull(captured);
            Assert.Equal("C:/img1.jpg", captured.FilePath);
            Assert.Equal("C:/img2.jpg", captured.AlternatePath);
        });
    }

    [Fact]
    public void Image2DoubleClick_PublishesEventWithImage2PathAsFilePath()
    {
        StaHelper.Run(() =>
        {
            // Arrange
            OpenImagePreviewPayload captured = null;

            var mockEvent = new Mock<OpenImagePreviewEvent>();
            mockEvent
                .Setup(e => e.Publish(It.IsAny<OpenImagePreviewPayload>()))
                .Callback<OpenImagePreviewPayload>(p => captured = p);

            var mockEa = new Mock<IEventAggregator>();
            mockEa.Setup(ea => ea.GetEvent<OpenImagePreviewEvent>())
                .Returns(mockEvent.Object);

            var pair = new ImagePair(100, mockEa.Object)
            {
                Image1 = new ImageInfo("C:/img1.jpg", null),
                Image2 = new ImageInfo("C:/img2.jpg", null)
            };

            // Act
            pair.Image2DoubleClick.Execute();

            // Assert
            Assert.NotNull(captured);
            Assert.Equal("C:/img2.jpg", captured.FilePath);
            Assert.Equal("C:/img1.jpg", captured.AlternatePath);
        });
    }

    [Fact]
    public void Image1DoubleClick_WithNullImages_PublishesNullPaths()
    {
        StaHelper.Run(() =>
        {
            // Arrange
            OpenImagePreviewPayload captured = null;

            var mockEvent = new Mock<OpenImagePreviewEvent>();
            mockEvent
                .Setup(e => e.Publish(It.IsAny<OpenImagePreviewPayload>()))
                .Callback<OpenImagePreviewPayload>(p => captured = p);

            var mockEa = new Mock<IEventAggregator>();
            mockEa.Setup(ea => ea.GetEvent<OpenImagePreviewEvent>())
                .Returns(mockEvent.Object);

            // Image1 and Image2 are intentionally left null
            var pair = new ImagePair(100, mockEa.Object);

            // Act
            pair.Image1DoubleClick.Execute();

            // Assert -- null-conditional operators yield null paths
            Assert.NotNull(captured);
            Assert.Null(captured.FilePath);
            Assert.Null(captured.AlternatePath);
        });
    }

    [Fact]
    public void Dispose_SetsImage1ToNull()
    {
        StaHelper.Run(() =>
        {
            // Arrange
            var ea   = BuildMockEventAggregator();
            var pair = new ImagePair(100, ea)
            {
                Image1 = new ImageInfo("C:/img1.jpg", null),
                Image2 = new ImageInfo("C:/img2.jpg", null)
            };

            // Act
            pair.Dispose();

            // Assert
            Assert.Null(pair.Image1);
        });
    }

    [Fact]
    public void Dispose_SetsImage2ToNull()
    {
        StaHelper.Run(() =>
        {
            // Arrange
            var ea   = BuildMockEventAggregator();
            var pair = new ImagePair(100, ea)
            {
                Image1 = new ImageInfo("C:/img1.jpg", null),
                Image2 = new ImageInfo("C:/img2.jpg", null)
            };

            // Act
            pair.Dispose();

            // Assert
            Assert.Null(pair.Image2);
        });
    }

    [Fact]
    public void Dispose_WhenImagesAlreadyNull_DoesNotThrow()
    {
        StaHelper.Run(() =>
        {
            var ea   = BuildMockEventAggregator();
            var pair = new ImagePair(100, ea);    // Image1/2 are null

            var ex = Record.Exception(() => pair.Dispose());
            Assert.Null(ex);
        });
    }

    [Fact]
    public void Constructor_WithNullEventAggregator_DoesNotThrow()
    {
        StaHelper.Run(() =>
        {
            // IEventAggregator is null-guarded inside the command lambdas
            var ex = Record.Exception(() => new ImagePair(100, null));
            Assert.Null(ex);
        });
    }

    [Fact]
    public void Image1DoubleClick_WithNullEventAggregator_DoesNotThrow()
    {
        StaHelper.Run(() =>
        {
            var pair = new ImagePair(100, null);

            // The null-conditional ?. inside the lambda must prevent a NRE
            var ex = Record.Exception(() => pair.Image1DoubleClick.Execute());
            Assert.Null(ex);
        });
    }

    [Fact]
    public void Image2DoubleClick_WithNullEventAggregator_DoesNotThrow()
    {
        StaHelper.Run(() =>
        {
            var pair = new ImagePair(100, null);

            var ex = Record.Exception(() => pair.Image2DoubleClick.Execute());
            Assert.Null(ex);
        });
    }

    [Fact]
    public void ThumbnailSize_CanBeUpdatedAfterConstruction()
    {
        StaHelper.Run(() =>
        {
            var ea   = BuildMockEventAggregator();
            var pair = new ImagePair(100, ea);

            pair.ThumbnailSize = 256;

            Assert.Equal(256, pair.ThumbnailSize);
        });
    }
}

// =====================================================================
// MouseWheelGesture Tests
// =====================================================================

public class MouseWheelGestureTests
{
    [Fact]
    public void Down_StaticProperty_ReturnsNonNullInstance()
    {
        // Act
        var gesture = MouseWheelGesture.Down;

        // Assert
        Assert.NotNull(gesture);
    }

    [Fact]
    public void Up_StaticProperty_ReturnsNonNullInstance()
    {
        // Act
        var gesture = MouseWheelGesture.Up;

        // Assert
        Assert.NotNull(gesture);
    }

    [Fact]
    public void Down_StaticProperty_ReturnsMouseWheelGestureInstance()
    {
        // Each call to the property creates a fresh instance
        var gesture = MouseWheelGesture.Down;

        Assert.IsType<MouseWheelGesture>(gesture);
    }

    [Fact]
    public void Up_StaticProperty_ReturnsMouseWheelGestureInstance()
    {
        var gesture = MouseWheelGesture.Up;

        Assert.IsType<MouseWheelGesture>(gesture);
    }

    [Fact]
    public void Down_StaticProperty_ReturnsDifferentInstancesOnRepeatedAccess()
    {
        // The property is a computed getter (=>) so each access creates a new object
        var g1 = MouseWheelGesture.Down;
        var g2 = MouseWheelGesture.Down;

        Assert.NotSame(g1, g2);
    }

    [Fact]
    public void Up_StaticProperty_ReturnsDifferentInstancesOnRepeatedAccess()
    {
        var g1 = MouseWheelGesture.Up;
        var g2 = MouseWheelGesture.Up;

        Assert.NotSame(g1, g2);
    }

    [Fact]
    public void Down_And_Up_ReturnDistinctInstances()
    {
        var down = MouseWheelGesture.Down;
        var up   = MouseWheelGesture.Up;

        // They represent opposite scroll directions and must be separate objects
        Assert.NotSame(down, up);
    }

    [Fact]
    public void Matches_WithNullInputEventArgs_ReturnsFalse()
    {
        // Arrange -- both Up and Down share the same Matches override
        var gesture = MouseWheelGesture.Down;

        // Act
        // base.Matches(target, null) returns false for a null event,
        // so the override must also return false before reaching Delta logic.
        var result = gesture.Matches(null, null);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Matches_Up_WithNullInputEventArgs_ReturnsFalse()
    {
        var gesture = MouseWheelGesture.Up;

        var result = gesture.Matches(null, null);

        Assert.False(result);
    }
}