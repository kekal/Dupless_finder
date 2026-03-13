using Bunit;
using DuplessFinder.Web.Components;
using DuplessFinder.Web.Models;
using DuplessFinder.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace DuplessFinder.Web.Tests.Pages;

public class IndexPageTests : IDisposable
{
    private readonly TestContext _ctx;
    private readonly Mock<IFileAccessService> _mockFileAccess;
    private readonly Mock<ICacheService> _mockCache;
    private readonly Mock<IOpenCvService> _mockOpenCv;
    private readonly Mock<ICalcOperations> _mockCalcOps;

    public IndexPageTests()
    {
        _ctx = new TestContext();
        _mockFileAccess = new Mock<IFileAccessService>();
        _mockCache = new Mock<ICacheService>();
        _mockOpenCv = new Mock<IOpenCvService>();
        _mockCalcOps = new Mock<ICalcOperations>();

        // Default setups
        _mockFileAccess.Setup(f => f.IsFileSystemAccessSupportedAsync()).ReturnsAsync(true);
        _mockCache.Setup(c => c.InitializeAsync()).Returns(Task.CompletedTask);
        _mockCache.Setup(c => c.GetThumbnailAsync(It.IsAny<string>())).ReturnsAsync((byte[]?)null);
        _mockCache.Setup(c => c.GetAllSimilarityResultsAsync()).ReturnsAsync(new List<SimilarityEntry>());
        _mockFileAccess.Setup(f => f.ReadFileBytesAsync(It.IsAny<string>())).ReturnsAsync(new byte[] { 1, 2, 3 });
        _mockOpenCv.Setup(o => o.GenerateThumbnailAsync(It.IsAny<byte[]>(), It.IsAny<int>())).ReturnsAsync(new byte[] { 255, 0, 0 });
        _mockOpenCv.Setup(o => o.EnsureLoadedAsync()).Returns(Task.CompletedTask);

        _ctx.Services.AddSingleton(_mockFileAccess.Object);
        _ctx.Services.AddSingleton(_mockCache.Object);
        _ctx.Services.AddSingleton(_mockOpenCv.Object);
        _ctx.Services.AddSingleton(_mockCalcOps.Object);
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    #region Initialization Tests

    [Fact]
    public void OnInit_FileSystemNotSupported_ShowsUnsupportedMessage()
    {
        // Arrange
        _mockFileAccess.Setup(f => f.IsFileSystemAccessSupportedAsync()).ReturnsAsync(false);

        // Act
        var cut = _ctx.RenderComponent<DuplessFinder.Web.Pages.Index>();
        cut.WaitForAssertion(() =>
        {
            cut.Find("span.toolbar-status").TextContent.Should().Contain("File System Access API is not supported");
        });
    }

    [Fact]
    public void OnInit_FileSystemSupported_InitializesCacheAndShowsReady()
    {
        // Act
        var cut = _ctx.RenderComponent<DuplessFinder.Web.Pages.Index>();
        cut.WaitForAssertion(() =>
        {
            cut.Find("span.toolbar-status").TextContent.Should().Be("Ready");
        });

        _mockCache.Verify(c => c.InitializeAsync(), Times.Once);
    }

    [Fact]
    public void OnInit_RendersToolbarWithCorrectParameters()
    {
        // Act
        var cut = _ctx.RenderComponent<DuplessFinder.Web.Pages.Index>();

        // Assert
        cut.Find(".toolbar").Should().NotBeNull();
        cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Open")).Should().NotBeNull();
        cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Analyze")).Should().NotBeNull();
    }

    [Fact]
    public void OnInit_InitiallyShowsThumbnailGrid()
    {
        var cut = _ctx.RenderComponent<DuplessFinder.Web.Pages.Index>();

        cut.FindComponents<ThumbnailGrid>().Should().HaveCount(1);
        cut.FindComponents<PairGrid>().Should().HaveCount(0);
    }

    [Fact]
    public void OnInit_NoPreviewVisible()
    {
        var cut = _ctx.RenderComponent<DuplessFinder.Web.Pages.Index>();
        cut.FindComponents<ImagePreview>().Should().HaveCount(0);
    }

    [Fact]
    public void OnInit_NoWarningVisible()
    {
        var cut = _ctx.RenderComponent<DuplessFinder.Web.Pages.Index>();
        cut.FindAll("div.alert.alert-warning").Should().BeEmpty();
    }

    #endregion

    #region OpenFolder Flow Tests

    [Fact]
    public void OpenFolder_NullDirectory_DoesNothing()
    {
        _mockFileAccess.Setup(f => f.PickDirectoryAsync()).ReturnsAsync((string?)null);

        var cut = _ctx.RenderComponent<DuplessFinder.Web.Pages.Index>();
        var openButton = cut.FindAll("button").First(b => b.TextContent.Contains("Open"));
        openButton.Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("span.toolbar-status").TextContent.Should().Be("Ready");
        });
    }

    [Fact]
    public void OpenFolder_NoImagesFound_ShowsMessage()
    {
        _mockFileAccess.Setup(f => f.PickDirectoryAsync()).ReturnsAsync("test_folder");
        _mockFileAccess.Setup(f => f.ScanImagesAsync(It.IsAny<bool>())).ReturnsAsync(new List<FileEntry>());

        var cut = _ctx.RenderComponent<DuplessFinder.Web.Pages.Index>();
        var openButton = cut.FindAll("button").First(b => b.TextContent.Contains("Open"));
        openButton.Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("span.toolbar-status").TextContent.Should().Contain("No images found");
        });
    }

    [Fact]
    public void OpenFolder_WithImages_LoadsThumbnailsAndShowsProgress()
    {
        var fileEntries = new List<FileEntry>
        {
            new("folder/img1.jpg", "img1.jpg", 1000, 12345),
            new("folder/img2.jpg", "img2.jpg", 2000, 12346)
        };

        var hashDict = new Dictionary<string, SiftResult>
        {
            { "folder/img1.jpg", new SiftResult(10, 128, new byte[1280]) },
            { "folder/img2.jpg", new SiftResult(10, 128, new byte[1280]) }
        };
        var pathToFp = new Dictionary<string, string>
        {
            { "folder/img1.jpg", "1000_12345" },
            { "folder/img2.jpg", "2000_12346" }
        };

        _mockFileAccess.Setup(f => f.PickDirectoryAsync()).ReturnsAsync("test_folder");
        _mockFileAccess.Setup(f => f.ScanImagesAsync(It.IsAny<bool>())).ReturnsAsync(fileEntries);
        _mockCache.Setup(c => c.GetThumbnailAsync(It.IsAny<string>())).ReturnsAsync(new byte[] { 255, 216, 255 });
        _mockCalcOps.Setup(o => o.CalcSiftHashesAsync(It.IsAny<IList<ImageInfo>>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((hashDict, pathToFp));
        _mockCalcOps.Setup(o => o.CreateMatchCollectionAsync(It.IsAny<Dictionary<string, SiftResult>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PairSimilarityInfo>());

        var cut = _ctx.RenderComponent<DuplessFinder.Web.Pages.Index>();
        cut.FindAll("button").First(b => b.TextContent.Contains("Open")).Click();

        // After open, the page loads thumbnails then auto-analyzes.
        // With no matches, final status should indicate "No similar pairs"
        cut.WaitForAssertion(() =>
        {
            var status = cut.Find("span.toolbar-status").TextContent;
            status.Should().Contain("No similar pairs");
        }, timeout: TimeSpan.FromSeconds(5));
    }

    #endregion

    #region Analysis Flow Tests

    [Fact]
    public void Analyze_FewerThan2Images_ShowsMessage()
    {
        var cut = _ctx.RenderComponent<DuplessFinder.Web.Pages.Index>();

        var analyzeButton = cut.FindAll("button").First(b => b.TextContent.Contains("Analyze"));
        analyzeButton.Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("span.toolbar-status").TextContent.Should().Be("Need at least 2 images to analyze.");
        });
    }

    [Fact]
    public void Analyze_ComputesHashesAndCreatesMatches()
    {
        var fileEntries = new List<FileEntry>
        {
            new("folder/img1.jpg", "img1.jpg", 1000, 12345),
            new("folder/img2.jpg", "img2.jpg", 2000, 12346),
            new("folder/img3.jpg", "img3.jpg", 3000, 12347)
        };

        var hashDict = new Dictionary<string, SiftResult>
        {
            { "folder/img1.jpg", new SiftResult(10, 128, new byte[1280]) },
            { "folder/img2.jpg", new SiftResult(10, 128, new byte[1280]) },
            { "folder/img3.jpg", new SiftResult(10, 128, new byte[1280]) }
        };
        var pathToFp = new Dictionary<string, string>
        {
            { "folder/img1.jpg", "1000_12345" },
            { "folder/img2.jpg", "2000_12346" },
            { "folder/img3.jpg", "3000_12347" }
        };

        var matches = new List<PairSimilarityInfo>
        {
            new("folder/img1.jpg", "folder/img2.jpg", "1000_12345", "2000_12346", 150.5),
            new("folder/img2.jpg", "folder/img3.jpg", "2000_12346", "3000_12347", 180.0)
        };

        _mockFileAccess.Setup(f => f.PickDirectoryAsync()).ReturnsAsync("test_folder");
        _mockFileAccess.Setup(f => f.ScanImagesAsync(It.IsAny<bool>())).ReturnsAsync(fileEntries);
        _mockCache.Setup(c => c.GetThumbnailAsync(It.IsAny<string>())).ReturnsAsync(new byte[] { 255, 216, 255 });
        _mockCalcOps.Setup(o => o.CalcSiftHashesAsync(It.IsAny<IList<ImageInfo>>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((hashDict, pathToFp));
        _mockCalcOps.Setup(o => o.CreateMatchCollectionAsync(It.IsAny<Dictionary<string, SiftResult>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matches);

        var cut = _ctx.RenderComponent<DuplessFinder.Web.Pages.Index>();
        cut.FindAll("button").First(b => b.TextContent.Contains("Open")).Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("span.toolbar-status").TextContent.Should().Contain("similar pairs");
        }, timeout: TimeSpan.FromSeconds(5));

        cut.Find("span.toolbar-status").TextContent.Should().Contain("2");
    }

    [Fact]
    public void Analyze_FiltersDegenerateScores()
    {
        var fileEntries = new List<FileEntry>
        {
            new("folder/img1.jpg", "img1.jpg", 1000, 12345),
            new("folder/img2.jpg", "img2.jpg", 2000, 12346)
        };

        var hashDict = new Dictionary<string, SiftResult>
        {
            { "folder/img1.jpg", new SiftResult(10, 128, new byte[1280]) },
            { "folder/img2.jpg", new SiftResult(10, 128, new byte[1280]) }
        };
        var pathToFp = new Dictionary<string, string>
        {
            { "folder/img1.jpg", "1000_12345" },
            { "folder/img2.jpg", "2000_12346" }
        };

        // All scores are degenerate (>=1000 or MaxValue)
        var rawMatches = new List<PairSimilarityInfo>
        {
            new("folder/img1.jpg", "folder/img2.jpg", "1000_12345", "2000_12346", 1500.0),
            new("folder/img1.jpg", "folder/img2.jpg", "1000_12345", "2000_12346", double.MaxValue)
        };

        _mockFileAccess.Setup(f => f.PickDirectoryAsync()).ReturnsAsync("test_folder");
        _mockFileAccess.Setup(f => f.ScanImagesAsync(It.IsAny<bool>())).ReturnsAsync(fileEntries);
        _mockCache.Setup(c => c.GetThumbnailAsync(It.IsAny<string>())).ReturnsAsync(new byte[] { 255, 216, 255 });
        _mockCalcOps.Setup(o => o.CalcSiftHashesAsync(It.IsAny<IList<ImageInfo>>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((hashDict, pathToFp));
        _mockCalcOps.Setup(o => o.CreateMatchCollectionAsync(It.IsAny<Dictionary<string, SiftResult>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawMatches);

        var cut = _ctx.RenderComponent<DuplessFinder.Web.Pages.Index>();
        cut.FindAll("button").First(b => b.TextContent.Contains("Open")).Click();

        cut.WaitForAssertion(() =>
        {
            var status = cut.Find("span.toolbar-status").TextContent;
            // Should show "No similar pairs" since all are filtered
            status.Should().Contain("No similar pairs");
        }, timeout: TimeSpan.FromSeconds(5));
    }

    #endregion

    #region Threshold Tests

    [Fact]
    public void ThresholdChange_FiltersResults()
    {
        var fileEntries = new List<FileEntry>
        {
            new("folder/img1.jpg", "img1.jpg", 1000, 12345),
            new("folder/img2.jpg", "img2.jpg", 2000, 12346),
            new("folder/img3.jpg", "img3.jpg", 3000, 12347)
        };

        var hashDict = new Dictionary<string, SiftResult>
        {
            { "folder/img1.jpg", new SiftResult(10, 128, new byte[1280]) },
            { "folder/img2.jpg", new SiftResult(10, 128, new byte[1280]) },
            { "folder/img3.jpg", new SiftResult(10, 128, new byte[1280]) }
        };
        var pathToFp = new Dictionary<string, string>
        {
            { "folder/img1.jpg", "1000_12345" },
            { "folder/img2.jpg", "2000_12346" },
            { "folder/img3.jpg", "3000_12347" }
        };

        // All scores below default threshold (200) so all 3 show initially
        var matches = new List<PairSimilarityInfo>
        {
            new("folder/img1.jpg", "folder/img2.jpg", "1000_12345", "2000_12346", 50.0),
            new("folder/img2.jpg", "folder/img3.jpg", "2000_12346", "3000_12347", 100.0),
            new("folder/img1.jpg", "folder/img3.jpg", "1000_12345", "3000_12347", 150.0)
        };

        _mockFileAccess.Setup(f => f.PickDirectoryAsync()).ReturnsAsync("test_folder");
        _mockFileAccess.Setup(f => f.ScanImagesAsync(It.IsAny<bool>())).ReturnsAsync(fileEntries);
        _mockCache.Setup(c => c.GetThumbnailAsync(It.IsAny<string>())).ReturnsAsync(new byte[] { 255, 216, 255 });
        _mockCalcOps.Setup(o => o.CalcSiftHashesAsync(It.IsAny<IList<ImageInfo>>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((hashDict, pathToFp));
        _mockCalcOps.Setup(o => o.CreateMatchCollectionAsync(It.IsAny<Dictionary<string, SiftResult>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matches);

        var cut = _ctx.RenderComponent<DuplessFinder.Web.Pages.Index>();

        // Load folder first - all 3 pairs visible with default threshold 200
        cut.FindAll("button").First(b => b.TextContent.Contains("Open")).Click();
        cut.WaitForAssertion(() =>
        {
            cut.Find("span.toolbar-status").TextContent.Should().Contain("3");
        }, timeout: TimeSpan.FromSeconds(5));

        // Change threshold to 80 (should filter to 1 pair: score 50.0 < 80)
        var slider = cut.Find("input[type='range']");
        slider.Input(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "80" });

        // Wait for debounce (200ms + buffer)
        cut.WaitForAssertion(() =>
        {
            cut.Find("span.toolbar-status").TextContent.Should().Contain("1");
        }, timeout: TimeSpan.FromSeconds(2));
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Component_Implements_IAsyncDisposable()
    {
        var cut = _ctx.RenderComponent<DuplessFinder.Web.Pages.Index>();
        cut.Instance.Should().BeAssignableTo<IAsyncDisposable>();
    }

    #endregion
}
