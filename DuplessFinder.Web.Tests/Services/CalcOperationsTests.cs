using DuplessFinder.Web.Models;
using DuplessFinder.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace DuplessFinder.Web.Tests.Services;

public class CalcOperationsTests
{
    private readonly Mock<IOpenCvService> _mockOpenCv;
    private readonly Mock<ICacheService> _mockCache;
    private readonly Mock<IFileAccessService> _mockFileAccess;
    private readonly Mock<ILogger<CalcOperations>> _mockLogger;
    private readonly CalcOperations _sut;

    public CalcOperationsTests()
    {
        _mockOpenCv = new Mock<IOpenCvService>();
        _mockCache = new Mock<ICacheService>();
        _mockFileAccess = new Mock<IFileAccessService>();
        _mockLogger = new Mock<ILogger<CalcOperations>>();

        _sut = new CalcOperations(
            _mockOpenCv.Object,
            _mockCache.Object,
            _mockFileAccess.Object,
            _mockLogger.Object);
    }

    #region CalcSiftHashesAsync Tests

    [Fact]
    public async Task CalcSiftHashesAsync_WithEmptyImageList_ReturnsEmptyDictionary()
    {
        // Arrange
        var images = new List<ImageInfo>();
        var progress = new TestProgress<double>();

        // Act
        var (hashDict, pathToFingerprint) = await _sut.CalcSiftHashesAsync(images, progress);

        // Assert
        hashDict.Should().BeEmpty();
        pathToFingerprint.Should().BeEmpty();
    }

    [Fact]
    public async Task CalcSiftHashesAsync_WhenCachedSiftAvailable_UsesCachedResult()
    {
        // Arrange
        var fileEntry = new FileEntry("path/img.jpg", "img.jpg", 1000, 12345);
        var image = new ImageInfo(fileEntry);
        var images = new List<ImageInfo> { image };
        var progress = new TestProgress<double>();

        var cachedDescriptor = new SiftDescriptorData(10, 128, new byte[1280]);
        _mockCache.Setup(c => c.GetSiftDescriptorAsync(image.Fingerprint))
            .ReturnsAsync(cachedDescriptor);

        // Act
        var (hashDict, _) = await _sut.CalcSiftHashesAsync(images, progress);

        // Assert
        hashDict.Should().ContainKey(image.Path);
        hashDict[image.Path].Should().Be(new SiftResult(10, 128, cachedDescriptor.Data));
        _mockOpenCv.Verify(o => o.ComputeSiftAsync(It.IsAny<byte[]>(), It.IsAny<string>()), Times.Never);
        _mockFileAccess.Verify(f => f.ReadFileBytesAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CalcSiftHashesAsync_WhenCacheMiss_ComputesAndStoresSift()
    {
        // Arrange
        var fileEntry = new FileEntry("path/img.jpg", "img.jpg", 1000, 12345);
        var image = new ImageInfo(fileEntry);
        var images = new List<ImageInfo> { image };
        var progress = new TestProgress<double>();

        var fileBytes = new byte[1024];
        var siftResult = new SiftResult(10, 128, new byte[1280]);

        _mockCache.Setup(c => c.GetSiftDescriptorAsync(image.Fingerprint))
            .ReturnsAsync((SiftDescriptorData?)null);
        _mockFileAccess.Setup(f => f.ReadFileBytesAsync(image.Path))
            .ReturnsAsync(fileBytes);
        _mockOpenCv.Setup(o => o.ComputeSiftAsync(fileBytes, image.Fingerprint))
            .ReturnsAsync(siftResult);

        // Act
        var (hashDict, _) = await _sut.CalcSiftHashesAsync(images, progress);

        // Assert
        hashDict.Should().ContainKey(image.Path);
        hashDict[image.Path].Should().Be(siftResult);
        _mockCache.Verify(c => c.PutSiftDescriptorAsync(image.Fingerprint, siftResult.DescriptorData, 10, 128), Times.Once);
    }

    [Fact]
    public async Task CalcSiftHashesAsync_WhenFileHasEmptyBytes_SkipsImage()
    {
        // Arrange
        var fileEntry = new FileEntry("path/img.jpg", "img.jpg", 1000, 12345);
        var image = new ImageInfo(fileEntry);
        var images = new List<ImageInfo> { image };
        var progress = new TestProgress<double>();

        _mockCache.Setup(c => c.GetSiftDescriptorAsync(image.Fingerprint))
            .ReturnsAsync((SiftDescriptorData?)null);
        _mockFileAccess.Setup(f => f.ReadFileBytesAsync(image.Path))
            .ReturnsAsync(Array.Empty<byte>());

        // Act
        var (hashDict, _) = await _sut.CalcSiftHashesAsync(images, progress);

        // Assert
        hashDict.Should().NotContainKey(image.Path);
        _mockOpenCv.Verify(o => o.ComputeSiftAsync(It.IsAny<byte[]>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CalcSiftHashesAsync_WhenComputeSiftReturnsNull_SkipsImage()
    {
        // Arrange
        var fileEntry = new FileEntry("path/img.jpg", "img.jpg", 1000, 12345);
        var image = new ImageInfo(fileEntry);
        var images = new List<ImageInfo> { image };
        var progress = new TestProgress<double>();

        var fileBytes = new byte[1024];

        _mockCache.Setup(c => c.GetSiftDescriptorAsync(image.Fingerprint))
            .ReturnsAsync((SiftDescriptorData?)null);
        _mockFileAccess.Setup(f => f.ReadFileBytesAsync(image.Path))
            .ReturnsAsync(fileBytes);
        _mockOpenCv.Setup(o => o.ComputeSiftAsync(fileBytes, image.Fingerprint))
            .ReturnsAsync((SiftResult?)null);

        // Act
        var (hashDict, _) = await _sut.CalcSiftHashesAsync(images, progress);

        // Assert
        hashDict.Should().NotContainKey(image.Path);
    }

    [Fact]
    public async Task CalcSiftHashesAsync_WhenSiftReturnsEmptyDescriptorData_SkipsImage()
    {
        // Arrange
        var fileEntry = new FileEntry("path/img.jpg", "img.jpg", 1000, 12345);
        var image = new ImageInfo(fileEntry);
        var images = new List<ImageInfo> { image };
        var progress = new TestProgress<double>();

        var fileBytes = new byte[1024];
        var siftResultWithEmptyData = new SiftResult(0, 128, Array.Empty<byte>());

        _mockCache.Setup(c => c.GetSiftDescriptorAsync(image.Fingerprint))
            .ReturnsAsync((SiftDescriptorData?)null);
        _mockFileAccess.Setup(f => f.ReadFileBytesAsync(image.Path))
            .ReturnsAsync(fileBytes);
        _mockOpenCv.Setup(o => o.ComputeSiftAsync(fileBytes, image.Fingerprint))
            .ReturnsAsync(siftResultWithEmptyData);

        // Act
        var (hashDict, _) = await _sut.CalcSiftHashesAsync(images, progress);

        // Assert
        hashDict.Should().NotContainKey(image.Path);
    }

    [Fact]
    public async Task CalcSiftHashesAsync_ReportsProgressCorrectly()
    {
        // Arrange
        var images = new List<ImageInfo>
        {
            new ImageInfo(new FileEntry("path/img1.jpg", "img1.jpg", 1000, 12345)),
            new ImageInfo(new FileEntry("path/img2.jpg", "img2.jpg", 2000, 23456))
        };
        var progress = new TestProgress<double>();

        var fileBytes = new byte[1024];
        var siftResult = new SiftResult(10, 128, new byte[1280]);

        _mockCache.Setup(c => c.GetSiftDescriptorAsync(It.IsAny<string>()))
            .ReturnsAsync((SiftDescriptorData?)null);
        _mockFileAccess.Setup(f => f.ReadFileBytesAsync(It.IsAny<string>()))
            .ReturnsAsync(fileBytes);
        _mockOpenCv.Setup(o => o.ComputeSiftAsync(It.IsAny<byte[]>(), It.IsAny<string>()))
            .ReturnsAsync(siftResult);

        // Act
        await _sut.CalcSiftHashesAsync(images, progress);

        // Assert
        progress.Values.Should().Contain(0); // Initial report
        progress.Values.Should().Contain(100); // Final report
        progress.Values.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CalcSiftHashesAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var fileEntry = new FileEntry("path/img.jpg", "img.jpg", 1000, 12345);
        var image = new ImageInfo(fileEntry);
        var images = new List<ImageInfo> { image };
        var progress = new TestProgress<double>();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _sut.CalcSiftHashesAsync(images, progress, cts.Token));
    }

    [Fact]
    public async Task CalcSiftHashesAsync_WhenImageErrorOccurs_ContinuesProcessingOtherImages()
    {
        // Arrange
        var image1 = new ImageInfo(new FileEntry("path/img1.jpg", "img1.jpg", 1000, 12345));
        var image2 = new ImageInfo(new FileEntry("path/img2.jpg", "img2.jpg", 2000, 23456));
        var images = new List<ImageInfo> { image1, image2 };
        var progress = new TestProgress<double>();

        var fileBytes = new byte[1024];
        var siftResult = new SiftResult(10, 128, new byte[1280]);

        _mockCache.Setup(c => c.GetSiftDescriptorAsync(image1.Fingerprint))
            .ThrowsAsync(new Exception("Cache error"));
        _mockCache.Setup(c => c.GetSiftDescriptorAsync(image2.Fingerprint))
            .ReturnsAsync((SiftDescriptorData?)null);
        _mockFileAccess.Setup(f => f.ReadFileBytesAsync(image2.Path))
            .ReturnsAsync(fileBytes);
        _mockOpenCv.Setup(o => o.ComputeSiftAsync(fileBytes, image2.Fingerprint))
            .ReturnsAsync(siftResult);

        // Act
        var (hashDict, _) = await _sut.CalcSiftHashesAsync(images, progress);

        // Assert
        hashDict.Should().ContainKey(image2.Path);
        hashDict.Should().NotContainKey(image1.Path);
        hashDict.Count.Should().Be(1);
    }

    [Fact]
    public async Task CalcSiftHashesAsync_WhenCacheStoreFailure_ContinuesProcessing()
    {
        // Arrange
        var fileEntry = new FileEntry("path/img.jpg", "img.jpg", 1000, 12345);
        var image = new ImageInfo(fileEntry);
        var images = new List<ImageInfo> { image };
        var progress = new TestProgress<double>();

        var fileBytes = new byte[1024];
        var siftResult = new SiftResult(10, 128, new byte[1280]);

        _mockCache.Setup(c => c.GetSiftDescriptorAsync(image.Fingerprint))
            .ReturnsAsync((SiftDescriptorData?)null);
        _mockFileAccess.Setup(f => f.ReadFileBytesAsync(image.Path))
            .ReturnsAsync(fileBytes);
        _mockOpenCv.Setup(o => o.ComputeSiftAsync(fileBytes, image.Fingerprint))
            .ReturnsAsync(siftResult);
        _mockCache.Setup(c => c.PutSiftDescriptorAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()))
            .ThrowsAsync(new Exception("Cache store failed"));

        // Act
        var (hashDict, _) = await _sut.CalcSiftHashesAsync(images, progress);

        // Assert
        hashDict.Should().ContainKey(image.Path);
        hashDict[image.Path].Should().Be(siftResult);
    }

    [Fact]
    public async Task CalcSiftHashesAsync_BuildsPathToFingerprintDictionary()
    {
        // Arrange
        var image1 = new ImageInfo(new FileEntry("path/img1.jpg", "img1.jpg", 1000, 12345));
        var image2 = new ImageInfo(new FileEntry("path/img2.jpg", "img2.jpg", 2000, 23456));
        var images = new List<ImageInfo> { image1, image2 };
        var progress = new TestProgress<double>();

        var fileBytes = new byte[1024];
        var siftResult = new SiftResult(10, 128, new byte[1280]);

        _mockCache.Setup(c => c.GetSiftDescriptorAsync(It.IsAny<string>()))
            .ReturnsAsync((SiftDescriptorData?)null);
        _mockFileAccess.Setup(f => f.ReadFileBytesAsync(It.IsAny<string>()))
            .ReturnsAsync(fileBytes);
        _mockOpenCv.Setup(o => o.ComputeSiftAsync(It.IsAny<byte[]>(), It.IsAny<string>()))
            .ReturnsAsync(siftResult);

        // Act
        var (_, pathToFingerprint) = await _sut.CalcSiftHashesAsync(images, progress);

        // Assert
        pathToFingerprint.Should().ContainKey(image1.Path);
        pathToFingerprint.Should().ContainKey(image2.Path);
        pathToFingerprint[image1.Path].Should().Be(image1.Fingerprint);
        pathToFingerprint[image2.Path].Should().Be(image2.Fingerprint);
    }

    [Fact]
    public async Task CalcSiftHashesAsync_ProcessesMultipleImagesSequentially()
    {
        // Arrange
        var images = new List<ImageInfo>();
        for (int i = 0; i < 5; i++)
        {
            images.Add(new ImageInfo(new FileEntry($"path/img{i}.jpg", $"img{i}.jpg", 1000 + i, 12345 + i)));
        }
        var progress = new TestProgress<double>();

        var fileBytes = new byte[1024];
        var siftResult = new SiftResult(10, 128, new byte[1280]);

        _mockCache.Setup(c => c.GetSiftDescriptorAsync(It.IsAny<string>()))
            .ReturnsAsync((SiftDescriptorData?)null);
        _mockFileAccess.Setup(f => f.ReadFileBytesAsync(It.IsAny<string>()))
            .ReturnsAsync(fileBytes);
        _mockOpenCv.Setup(o => o.ComputeSiftAsync(It.IsAny<byte[]>(), It.IsAny<string>()))
            .ReturnsAsync(siftResult);

        // Act
        var (hashDict, _) = await _sut.CalcSiftHashesAsync(images, progress);

        // Assert
        hashDict.Count.Should().Be(5);
        _mockOpenCv.Verify(o => o.ComputeSiftAsync(It.IsAny<byte[]>(), It.IsAny<string>()), Times.Exactly(5));
    }

    [Fact]
    public async Task CalcSiftHashesAsync_ProgressIsThrottled()
    {
        // Arrange
        var images = new List<ImageInfo>();
        // Create 1000 images to test throttling
        for (int i = 0; i < 1000; i++)
        {
            images.Add(new ImageInfo(new FileEntry($"path/img{i}.jpg", $"img{i}.jpg", 1000 + i, 12345 + i)));
        }
        var progress = new TestProgress<double>();

        var cachedDescriptor = new SiftDescriptorData(10, 128, new byte[1280]);
        _mockCache.Setup(c => c.GetSiftDescriptorAsync(It.IsAny<string>()))
            .ReturnsAsync(cachedDescriptor);

        // Act
        await _sut.CalcSiftHashesAsync(images, progress);

        // Assert
        // With throttling, progress reports should be significantly less than total images
        // For 1000 items, step = 1000/500 + 1 = 3, so reports should be every 3 items plus 100 at end
        // This should result in much fewer reports than 1000
        progress.Values.Count.Should().BeLessThan(500);
        progress.Values.Should().Contain(0);
        progress.Values.Should().Contain(100);
    }

    #endregion

    #region CreateMatchCollectionAsync Tests

    [Fact]
    public async Task CreateMatchCollectionAsync_WithEmptyHashDict_ReturnsEmptyList()
    {
        // Arrange
        var hashDict = new Dictionary<string, SiftResult>();
        var pathToFingerprint = new Dictionary<string, string>();
        var progress = new TestProgress<double>();

        // Act
        var result = await _sut.CreateMatchCollectionAsync(hashDict, pathToFingerprint, progress);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateMatchCollectionAsync_WithSingleEntry_ReturnsEmptyList()
    {
        // Arrange
        var hashDict = new Dictionary<string, SiftResult>
        {
            { "path/img1.jpg", new SiftResult(10, 128, new byte[1280]) }
        };
        var pathToFingerprint = new Dictionary<string, string>
        {
            { "path/img1.jpg", "1000_12345" }
        };
        var progress = new TestProgress<double>();

        // Act
        var result = await _sut.CreateMatchCollectionAsync(hashDict, pathToFingerprint, progress);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateMatchCollectionAsync_CreatesCorrectNumberOfPairs()
    {
        // Arrange
        var hashDict = new Dictionary<string, SiftResult>
        {
            { "path/img1.jpg", new SiftResult(10, 128, new byte[1280]) },
            { "path/img2.jpg", new SiftResult(10, 128, new byte[1280]) },
            { "path/img3.jpg", new SiftResult(10, 128, new byte[1280]) }
        };
        var pathToFingerprint = new Dictionary<string, string>
        {
            { "path/img1.jpg", "1000_12345" },
            { "path/img2.jpg", "2000_23456" },
            { "path/img3.jpg", "3000_34567" }
        };
        var progress = new TestProgress<double>();

        _mockOpenCv.Setup(o => o.MatchPairAsync(It.IsAny<SiftResult>(), It.IsAny<SiftResult>()))
            .ReturnsAsync(50.0);

        // Act
        var result = await _sut.CreateMatchCollectionAsync(hashDict, pathToFingerprint, progress);

        // Assert
        // For 3 items: 3 * (3-1) / 2 = 3 pairs
        result.Count.Should().Be(3);
        _mockOpenCv.Verify(o => o.MatchPairAsync(It.IsAny<SiftResult>(), It.IsAny<SiftResult>()), Times.Exactly(3));
    }

    [Fact]
    public async Task CreateMatchCollectionAsync_CallsMatchPairForEachPair()
    {
        // Arrange
        var hashDict = new Dictionary<string, SiftResult>
        {
            { "path/img1.jpg", new SiftResult(10, 128, new byte[1280]) },
            { "path/img2.jpg", new SiftResult(10, 128, new byte[1280]) }
        };
        var pathToFingerprint = new Dictionary<string, string>
        {
            { "path/img1.jpg", "1000_12345" },
            { "path/img2.jpg", "2000_23456" }
        };
        var progress = new TestProgress<double>();

        var callCount = 0;
        _mockOpenCv.Setup(o => o.MatchPairAsync(It.IsAny<SiftResult>(), It.IsAny<SiftResult>()))
            .Callback<SiftResult, SiftResult>((s1, s2) => callCount++)
            .ReturnsAsync(50.0);

        // Act
        await _sut.CreateMatchCollectionAsync(hashDict, pathToFingerprint, progress);

        // Assert
        callCount.Should().Be(1);
        _mockOpenCv.Verify(o => o.MatchPairAsync(It.IsAny<SiftResult>(), It.IsAny<SiftResult>()), Times.Once);
    }

    [Fact]
    public async Task CreateMatchCollectionAsync_ReturnsResultsOrderedByScore()
    {
        // Arrange
        var hashDict = new Dictionary<string, SiftResult>
        {
            { "path/img1.jpg", new SiftResult(10, 128, new byte[1280]) },
            { "path/img2.jpg", new SiftResult(10, 128, new byte[1280]) },
            { "path/img3.jpg", new SiftResult(10, 128, new byte[1280]) }
        };
        var pathToFingerprint = new Dictionary<string, string>
        {
            { "path/img1.jpg", "1000_12345" },
            { "path/img2.jpg", "2000_23456" },
            { "path/img3.jpg", "3000_34567" }
        };
        var progress = new TestProgress<double>();

        var scoreSequence = new[] { 100.0, 50.0, 75.0 };
        var callIndex = 0;
        _mockOpenCv.Setup(o => o.MatchPairAsync(It.IsAny<SiftResult>(), It.IsAny<SiftResult>()))
            .Returns(() => Task.FromResult(scoreSequence[callIndex++]));

        // Act
        var result = await _sut.CreateMatchCollectionAsync(hashDict, pathToFingerprint, progress);

        // Assert
        result.Should().HaveCount(3);
        result[0].Score.Should().Be(50.0);
        result[1].Score.Should().Be(75.0);
        result[2].Score.Should().Be(100.0);
    }

    [Fact]
    public async Task CreateMatchCollectionAsync_HandlesPairMatchFailureGracefully()
    {
        // Arrange
        var hashDict = new Dictionary<string, SiftResult>
        {
            { "path/img1.jpg", new SiftResult(10, 128, new byte[1280]) },
            { "path/img2.jpg", new SiftResult(10, 128, new byte[1280]) },
            { "path/img3.jpg", new SiftResult(10, 128, new byte[1280]) }
        };
        var pathToFingerprint = new Dictionary<string, string>
        {
            { "path/img1.jpg", "1000_12345" },
            { "path/img2.jpg", "2000_23456" },
            { "path/img3.jpg", "3000_34567" }
        };
        var progress = new TestProgress<double>();

        var callCount = 0;
        _mockOpenCv.Setup(o => o.MatchPairAsync(It.IsAny<SiftResult>(), It.IsAny<SiftResult>()))
            .Callback<SiftResult, SiftResult>((s1, s2) =>
            {
                callCount++;
                if (callCount == 2) throw new Exception("Match failed");
            })
            .ReturnsAsync(50.0);

        // Act
        var result = await _sut.CreateMatchCollectionAsync(hashDict, pathToFingerprint, progress);

        // Assert
        // Should continue despite the failure, resulting in 2 successful pairs instead of 3
        result.Count.Should().Be(2);
        _mockOpenCv.Verify(o => o.MatchPairAsync(It.IsAny<SiftResult>(), It.IsAny<SiftResult>()), Times.Exactly(3));
    }

    [Fact]
    public async Task CreateMatchCollectionAsync_ReportsProgressCorrectly()
    {
        // Arrange
        var hashDict = new Dictionary<string, SiftResult>
        {
            { "path/img1.jpg", new SiftResult(10, 128, new byte[1280]) },
            { "path/img2.jpg", new SiftResult(10, 128, new byte[1280]) }
        };
        var pathToFingerprint = new Dictionary<string, string>
        {
            { "path/img1.jpg", "1000_12345" },
            { "path/img2.jpg", "2000_23456" }
        };
        var progress = new TestProgress<double>();

        _mockOpenCv.Setup(o => o.MatchPairAsync(It.IsAny<SiftResult>(), It.IsAny<SiftResult>()))
            .ReturnsAsync(50.0);

        // Act
        await _sut.CreateMatchCollectionAsync(hashDict, pathToFingerprint, progress);

        // Assert
        progress.Values.Should().Contain(0);
        progress.Values.Should().Contain(100);
        progress.Values.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateMatchCollectionAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var hashDict = new Dictionary<string, SiftResult>
        {
            { "path/img1.jpg", new SiftResult(10, 128, new byte[1280]) },
            { "path/img2.jpg", new SiftResult(10, 128, new byte[1280]) }
        };
        var pathToFingerprint = new Dictionary<string, string>
        {
            { "path/img1.jpg", "1000_12345" },
            { "path/img2.jpg", "2000_23456" }
        };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _sut.CreateMatchCollectionAsync(hashDict, pathToFingerprint, null, cts.Token));
    }

    [Fact]
    public async Task CreateMatchCollectionAsync_MapsFingerprintsCorrectly()
    {
        // Arrange
        var hashDict = new Dictionary<string, SiftResult>
        {
            { "path/img1.jpg", new SiftResult(10, 128, new byte[1280]) },
            { "path/img2.jpg", new SiftResult(10, 128, new byte[1280]) }
        };
        var pathToFingerprint = new Dictionary<string, string>
        {
            { "path/img1.jpg", "fp1" },
            { "path/img2.jpg", "fp2" }
        };
        var progress = new TestProgress<double>();

        _mockOpenCv.Setup(o => o.MatchPairAsync(It.IsAny<SiftResult>(), It.IsAny<SiftResult>()))
            .ReturnsAsync(50.0);

        // Act
        var result = await _sut.CreateMatchCollectionAsync(hashDict, pathToFingerprint, progress);

        // Assert
        result.Should().HaveCount(1);
        result[0].Fingerprint1.Should().Be("fp1");
        result[0].Fingerprint2.Should().Be("fp2");
    }

    [Fact]
    public async Task CreateMatchCollectionAsync_UsesFallbackPathWhenFingerprintMissing()
    {
        // Arrange
        var hashDict = new Dictionary<string, SiftResult>
        {
            { "path/img1.jpg", new SiftResult(10, 128, new byte[1280]) },
            { "path/img2.jpg", new SiftResult(10, 128, new byte[1280]) }
        };
        var pathToFingerprint = new Dictionary<string, string>
        {
            // Only add fingerprint for img1, not img2
            { "path/img1.jpg", "fp1" }
        };
        var progress = new TestProgress<double>();

        _mockOpenCv.Setup(o => o.MatchPairAsync(It.IsAny<SiftResult>(), It.IsAny<SiftResult>()))
            .ReturnsAsync(50.0);

        // Act
        var result = await _sut.CreateMatchCollectionAsync(hashDict, pathToFingerprint, progress);

        // Assert
        result.Should().HaveCount(1);
        result[0].Fingerprint1.Should().Be("fp1");
        result[0].Fingerprint2.Should().Be("path/img2.jpg"); // Falls back to path
    }

    [Fact]
    public async Task CreateMatchCollectionAsync_AssignsScoresToPairSimilarityInfo()
    {
        // Arrange
        var hashDict = new Dictionary<string, SiftResult>
        {
            { "path/img1.jpg", new SiftResult(10, 128, new byte[1280]) },
            { "path/img2.jpg", new SiftResult(10, 128, new byte[1280]) }
        };
        var pathToFingerprint = new Dictionary<string, string>
        {
            { "path/img1.jpg", "fp1" },
            { "path/img2.jpg", "fp2" }
        };
        var progress = new TestProgress<double>();

        var expectedScore = 42.5;
        _mockOpenCv.Setup(o => o.MatchPairAsync(It.IsAny<SiftResult>(), It.IsAny<SiftResult>()))
            .ReturnsAsync(expectedScore);

        // Act
        var result = await _sut.CreateMatchCollectionAsync(hashDict, pathToFingerprint, progress);

        // Assert
        result.Should().HaveCount(1);
        result[0].Score.Should().Be(expectedScore);
    }

    [Fact]
    public async Task CreateMatchCollectionAsync_WithLargeSet_ThrottlesProgress()
    {
        // Arrange
        var hashDict = new Dictionary<string, SiftResult>();
        var pathToFingerprint = new Dictionary<string, string>();

        // Create 20 images = 190 pairs
        for (int i = 0; i < 20; i++)
        {
            var path = $"path/img{i}.jpg";
            hashDict[path] = new SiftResult(10, 128, new byte[1280]);
            pathToFingerprint[path] = $"fp{i}";
        }

        var progress = new TestProgress<double>();

        _mockOpenCv.Setup(o => o.MatchPairAsync(It.IsAny<SiftResult>(), It.IsAny<SiftResult>()))
            .ReturnsAsync(50.0);

        // Act
        await _sut.CreateMatchCollectionAsync(hashDict, pathToFingerprint, progress);

        // Assert
        // With 190 pairs, step = 190/500 + 1 = 1, so reports should be frequent but throttled
        progress.Values.Should().Contain(0);
        progress.Values.Should().Contain(100);
        progress.Values.Count.Should().BeLessThan(200);
    }

    #endregion

    #region Private Helper Class

    /// <summary>
    /// Test implementation of IProgress that collects all reported values synchronously.
    /// </summary>
    private class TestProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = new();

        public void Report(T value)
        {
            Values.Add(value);
        }
    }

    #endregion
}
