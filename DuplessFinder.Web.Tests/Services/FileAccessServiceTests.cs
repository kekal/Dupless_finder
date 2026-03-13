using DuplessFinder.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Moq;
using Xunit;

namespace DuplessFinder.Web.Tests.Services;

public class FileAccessServiceTests
{
    private Mock<IJSRuntime> CreateMockJSRuntime()
    {
        var mockJs = new Mock<IJSRuntime>();
        var mockModule = new Mock<IJSObjectReference>();

        mockJs.Setup(j => j.InvokeAsync<IJSObjectReference>("import", It.IsAny<object[]>()))
            .ReturnsAsync(mockModule.Object);

        return mockJs;
    }

    private Mock<ILogger<FileAccessService>> CreateMockLogger()
    {
        return new Mock<ILogger<FileAccessService>>();
    }

    private FileAccessService CreateService(Mock<IJSRuntime>? mockJs = null, Mock<ILogger<FileAccessService>>? mockLogger = null)
    {
        mockJs ??= CreateMockJSRuntime();
        mockLogger ??= CreateMockLogger();

        return new FileAccessService(mockJs.Object, mockLogger.Object);
    }

    #region ValidateRelativePath Tests

    [Fact]
    public void ValidateRelativePath_ThrowsArgumentException_ForPathContainingDoubleDot()
    {
        var service = CreateService();

        // Use reflection to call private ValidateRelativePath method
        var method = typeof(FileAccessService).GetMethod(
            "ValidateRelativePath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        );

        method?.Invoking(m => m?.Invoke(null, new object[] { "../etc/passwd" }))
            .Should().Throw<System.Reflection.TargetInvocationException>()
            .WithInnerException<ArgumentException>();
    }

    [Fact]
    public void ValidateRelativePath_Succeeds_ForValidPath()
    {
        var service = CreateService();

        var method = typeof(FileAccessService).GetMethod(
            "ValidateRelativePath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        );

        // Should not throw
        method?.Invoke(null, new object[] { "folder/image.jpg" });
    }

    [Fact]
    public void ValidateRelativePath_Succeeds_ForPathWithoutTraversal()
    {
        var service = CreateService();

        var method = typeof(FileAccessService).GetMethod(
            "ValidateRelativePath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        );

        // Should not throw
        method?.Invoke(null, new object[] { "myFolder/subFolder/image.jpg" });
    }

    #endregion

    #region ReadFileBytesAsync Tests

    [Fact]
    public async Task ReadFileBytesAsync_CallsValidateRelativePath()
    {
        var mockJs = CreateMockJSRuntime();
        var mockModule = new Mock<IJSObjectReference>();
        mockJs.Setup(j => j.InvokeAsync<IJSObjectReference>("import", It.IsAny<object[]>()))
            .ReturnsAsync(mockModule.Object);
        mockModule.Setup(m => m.InvokeAsync<byte[]>("readFileBytes", It.IsAny<object[]>()))
            .ReturnsAsync(new byte[] { 0xFF, 0xD8 });

        var service = CreateService(mockJs);

        // Valid path should succeed
        await service.ReadFileBytesAsync("folder/image.jpg");

        // Invalid path (with ..) should throw
        await service.Invoking(s => s.ReadFileBytesAsync("../etc/passwd"))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReadFileBytesAsync_ReturnsFileBytes_OnSuccess()
    {
        var mockJs = CreateMockJSRuntime();
        var mockModule = new Mock<IJSObjectReference>();
        var expectedBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };

        mockJs.Setup(j => j.InvokeAsync<IJSObjectReference>("import", It.IsAny<object[]>()))
            .ReturnsAsync(mockModule.Object);
        mockModule.Setup(m => m.InvokeAsync<byte[]>("readFileBytes", It.IsAny<object[]>()))
            .ReturnsAsync(expectedBytes);

        var service = CreateService(mockJs);

        var result = await service.ReadFileBytesAsync("images/photo.jpg");

        result.Should().Equal(expectedBytes);
    }

    [Fact]
    public async Task ReadFileBytesAsync_ThrowsObjectDisposedException_WhenDisposed()
    {
        var service = CreateService();
        await service.DisposeAsync();

        await service.Invoking(s => s.ReadFileBytesAsync("image.jpg"))
            .Should().ThrowAsync<ObjectDisposedException>();
    }

    #endregion

    #region MoveToDeletedAsync Tests

    [Fact]
    public async Task MoveToDeletedAsync_CallsValidateRelativePath()
    {
        var mockJs = CreateMockJSRuntime();
        var mockModule = new Mock<IJSObjectReference>();
        mockJs.Setup(j => j.InvokeAsync<IJSObjectReference>("import", It.IsAny<object[]>()))
            .ReturnsAsync(mockModule.Object);
        mockModule.Setup(m => m.InvokeAsync<string>("moveToDeleted", It.IsAny<object[]>()))
            .ReturnsAsync("moved_name");

        var service = CreateService(mockJs);

        // Valid path should succeed
        await service.MoveToDeletedAsync("folder/image.jpg");

        // Invalid path (with ..) should throw
        await service.Invoking(s => s.MoveToDeletedAsync("../etc/passwd"))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task MoveToDeletedAsync_ReturnsDeletedName()
    {
        var mockJs = CreateMockJSRuntime();
        var mockModule = new Mock<IJSObjectReference>();

        mockJs.Setup(j => j.InvokeAsync<IJSObjectReference>("import", It.IsAny<object[]>()))
            .ReturnsAsync(mockModule.Object);
        mockModule.Setup(m => m.InvokeAsync<string>("moveToDeleted", It.IsAny<object[]>()))
            .ReturnsAsync("2024_03_13_image.jpg");

        var service = CreateService(mockJs);

        var result = await service.MoveToDeletedAsync("images/photo.jpg");

        result.Should().Be("2024_03_13_image.jpg");
    }

    [Fact]
    public async Task MoveToDeletedAsync_ThrowsObjectDisposedException_WhenDisposed()
    {
        var service = CreateService();
        await service.DisposeAsync();

        await service.Invoking(s => s.MoveToDeletedAsync("image.jpg"))
            .Should().ThrowAsync<ObjectDisposedException>();
    }

    #endregion

    #region RestoreFromDeletedAsync Tests

    [Fact]
    public async Task RestoreFromDeletedAsync_CallsValidateRelativePath()
    {
        var mockJs = CreateMockJSRuntime();
        var mockModule = new Mock<IJSObjectReference>();
        mockJs.Setup(j => j.InvokeAsync<IJSObjectReference>("import", It.IsAny<object[]>()))
            .ReturnsAsync(mockModule.Object);
        // Use InvokeAsync<IJSVoidResult> instead of the extension method InvokeVoidAsync
        mockModule.Setup(m => m.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>("restoreFromDeleted", It.IsAny<object[]>()))
            .ReturnsAsync(default(Microsoft.JSInterop.Infrastructure.IJSVoidResult)!);

        var service = CreateService(mockJs);

        // Valid path should succeed
        await service.RestoreFromDeletedAsync("folder/image.jpg", "deleted_name");

        // Invalid path (with ..) should throw
        await service.Invoking(s => s.RestoreFromDeletedAsync("../etc/passwd", "deleted_name"))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RestoreFromDeletedAsync_InvokesCorrectlyWithBothParameters()
    {
        var mockJs = CreateMockJSRuntime();
        var mockModule = new Mock<IJSObjectReference>();

        mockJs.Setup(j => j.InvokeAsync<IJSObjectReference>("import", It.IsAny<object[]>()))
            .ReturnsAsync(mockModule.Object);
        mockModule.Setup(m => m.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>("restoreFromDeleted", It.IsAny<object[]>()))
            .ReturnsAsync(default(Microsoft.JSInterop.Infrastructure.IJSVoidResult)!);

        var service = CreateService(mockJs);

        await service.RestoreFromDeletedAsync("images/photo.jpg", "2024_03_13_photo.jpg");

        mockModule.Verify(m => m.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>("restoreFromDeleted",
            It.Is<object[]>(o => o.Length == 2 && o[0].Equals("images/photo.jpg") && o[1].Equals("2024_03_13_photo.jpg"))),
            Times.Once);
    }

    [Fact]
    public async Task RestoreFromDeletedAsync_ThrowsObjectDisposedException_WhenDisposed()
    {
        var service = CreateService();
        await service.DisposeAsync();

        await service.Invoking(s => s.RestoreFromDeletedAsync("image.jpg", "deleted"))
            .Should().ThrowAsync<ObjectDisposedException>();
    }

    #endregion

    #region CreateObjectUrlAsync Tests

    [Fact]
    public async Task CreateObjectUrlAsync_CallsValidateRelativePath()
    {
        var mockJs = CreateMockJSRuntime();
        var mockModule = new Mock<IJSObjectReference>();
        mockJs.Setup(j => j.InvokeAsync<IJSObjectReference>("import", It.IsAny<object[]>()))
            .ReturnsAsync(mockModule.Object);
        mockModule.Setup(m => m.InvokeAsync<string>("createObjectUrl", It.IsAny<object[]>()))
            .ReturnsAsync("blob:http://localhost");

        var service = CreateService(mockJs);

        // Valid path should succeed
        await service.CreateObjectUrlAsync("folder/image.jpg");

        // Invalid path (with ..) should throw
        await service.Invoking(s => s.CreateObjectUrlAsync("../etc/passwd"))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateObjectUrlAsync_ReturnsObjectUrl()
    {
        var mockJs = CreateMockJSRuntime();
        var mockModule = new Mock<IJSObjectReference>();
        var expectedUrl = "blob:http://localhost:5000/abc123";

        mockJs.Setup(j => j.InvokeAsync<IJSObjectReference>("import", It.IsAny<object[]>()))
            .ReturnsAsync(mockModule.Object);
        mockModule.Setup(m => m.InvokeAsync<string>("createObjectUrl", It.IsAny<object[]>()))
            .ReturnsAsync(expectedUrl);

        var service = CreateService(mockJs);

        var result = await service.CreateObjectUrlAsync("images/photo.jpg");

        result.Should().Be(expectedUrl);
    }

    [Fact]
    public async Task CreateObjectUrlAsync_ThrowsObjectDisposedException_WhenDisposed()
    {
        var service = CreateService();
        await service.DisposeAsync();

        await service.Invoking(s => s.CreateObjectUrlAsync("image.jpg"))
            .Should().ThrowAsync<ObjectDisposedException>();
    }

    #endregion

    #region PickDirectoryAsync Tests

    [Fact]
    public async Task PickDirectoryAsync_ThrowsObjectDisposedException_WhenDisposed()
    {
        var service = CreateService();
        await service.DisposeAsync();

        await service.Invoking(s => s.PickDirectoryAsync())
            .Should().ThrowAsync<ObjectDisposedException>();
    }

    #endregion

    #region ScanImagesAsync Tests

    [Fact]
    public async Task ScanImagesAsync_ThrowsObjectDisposedException_WhenDisposed()
    {
        var service = CreateService();
        await service.DisposeAsync();

        await service.Invoking(s => s.ScanImagesAsync(false))
            .Should().ThrowAsync<ObjectDisposedException>();
    }

    #endregion

    #region IsFileSystemAccessSupportedAsync Tests

    [Fact]
    public async Task IsFileSystemAccessSupportedAsync_ThrowsObjectDisposedException_WhenDisposed()
    {
        var service = CreateService();
        await service.DisposeAsync();

        await service.Invoking(s => s.IsFileSystemAccessSupportedAsync())
            .Should().ThrowAsync<ObjectDisposedException>();
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes_Safely()
    {
        var service = CreateService();

        // Should not throw
        await service.DisposeAsync();
        await service.DisposeAsync();
        await service.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_SetsDisposedFlag()
    {
        var service = CreateService();

        await service.DisposeAsync();

        // Verify that subsequent calls throw ObjectDisposedException
        await service.Invoking(s => s.PickDirectoryAsync())
            .Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task DisposeAsync_DisposesJSModule()
    {
        var mockJs = CreateMockJSRuntime();
        var mockModule = new Mock<IJSObjectReference>();

        mockJs.Setup(j => j.InvokeAsync<IJSObjectReference>("import", It.IsAny<object[]>()))
            .ReturnsAsync(mockModule.Object);

        var service = CreateService(mockJs);

        // Force module load
        await service.PickDirectoryAsync();

        // Dispose
        await service.DisposeAsync();

        // Verify module was disposed
        mockModule.Verify(m => m.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_HandleJSDisconnectedException_Gracefully()
    {
        var mockJs = CreateMockJSRuntime();
        var mockModule = new Mock<IJSObjectReference>();

        mockJs.Setup(j => j.InvokeAsync<IJSObjectReference>("import", It.IsAny<object[]>()))
            .ReturnsAsync(mockModule.Object);

        // Module.DisposeAsync throws JSDisconnectedException
        mockModule.Setup(m => m.DisposeAsync())
            .ThrowsAsync(new JSDisconnectedException("Circuit disconnected"));

        var service = CreateService(mockJs);

        // Force module load
        await service.PickDirectoryAsync();

        // Should not throw even though module.DisposeAsync throws
        await service.DisposeAsync();
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task GetModuleAsync_ThreadSafeDuringConcurrentCalls()
    {
        var mockJs = CreateMockJSRuntime();
        var callCount = 0;
        var mockModule = new Mock<IJSObjectReference>();

        mockJs.Setup(j => j.InvokeAsync<IJSObjectReference>("import", It.IsAny<object[]>()))
            .Callback(() => Interlocked.Increment(ref callCount))
            .ReturnsAsync(mockModule.Object);

        var service = CreateService(mockJs);

        // Call multiple methods concurrently that trigger module loading
        var tasks = new Task[]
        {
            service.PickDirectoryAsync(),
            service.ScanImagesAsync(false),
            service.IsFileSystemAccessSupportedAsync()
        };

        await Task.WhenAll(tasks);

        // Module should only be imported once due to locking
        callCount.Should().Be(1);
    }

    #endregion
}
