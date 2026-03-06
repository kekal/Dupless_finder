using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Dupples_finder_UI.Data;
using Dupples_finder_UI.Data.Entities;
using Dupples_finder_UI.Services.Interfaces;
using OpenCvSharp;
using Xunit;

namespace Dupless_finder_Tests;

/// <summary>
/// xUnit tests for the PhotoDbService data layer.
/// Uses temporary SQLite database files for isolation.
/// </summary>
public class PhotoDbServiceTests : IDisposable
{
    private readonly string _tempDbPath;

    public PhotoDbServiceTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.db");
    }

    public void Dispose()
    {
        if (File.Exists(_tempDbPath))
        {
            try
            {
                File.Delete(_tempDbPath);
            }
            catch
            {
                // Ignore cleanup failures
            }
        }
    }

    [Fact]
    public async Task InitializeAsync_CreatesDatabase_Success()
    {
        // Arrange
        using IPhotoDbService service = new PhotoDbService();
        // Act
        await service.InitializeAsync(_tempDbPath);

        // Assert
        Assert.True(service.IsAvailable);
        Assert.True(File.Exists(_tempDbPath));
    }

    [Fact]
    public async Task GetCachedPhotoByFingerprint_ReturnsNull_WhenNotCached()
    {
        // Arrange
        using IPhotoDbService service = new PhotoDbService();
        await service.InitializeAsync(_tempDbPath);

        // Act
        var result = await service.GetCachedPhotoByFingerprintAsync(
            fileSize: 1000,
            lastModifiedUtc: DateTime.UtcNow);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CachePhotoHash_InsertsNewPhoto_Success()
    {
        // Arrange
        using IPhotoDbService service = new PhotoDbService();
        await service.InitializeAsync(_tempDbPath);
        var fileSize = 2048L;
        var lastMod = DateTime.UtcNow;
        var descriptors = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        var result = await service.CachePhotoHashAsync(
            filePath: "/path/to/photo1.jpg",
            descriptors: descriptors,
            rows: 10,
            cols: 128,
            fileSize: fileSize,
            lastModifiedUtc: lastMod);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("/path/to/photo1.jpg", result.FilePath);
        Assert.Equal(fileSize, result.FileSize);
        Assert.Equal(lastMod, result.LastModifiedUtc);
        Assert.Equal(descriptors, result.SiftDescriptors);
        Assert.Equal(10, result.DescriptorRows);
        Assert.Equal(128, result.DescriptorCols);
    }

    [Fact]
    public async Task CachePhotoHash_UpdatesExisting_WhenFingerprintMatches()
    {
        // Arrange
        using IPhotoDbService service = new PhotoDbService();
        await service.InitializeAsync(_tempDbPath);
        var fileSize = 3000L;
        var lastMod = DateTime.UtcNow;
        var descriptors1 = new byte[] { 1, 2, 3 };
        var descriptors2 = new byte[] { 4, 5, 6, 7 };

        // Insert first photo
        var result1 = await service.CachePhotoHashAsync(
            filePath: "/path/to/photo1.jpg",
            descriptors: descriptors1,
            rows: 5,
            cols: 128,
            fileSize: fileSize,
            lastModifiedUtc: lastMod);

        var id1 = result1.Id;

        // Act: Insert photo with same fingerprint (fileSize + lastMod)
        var result2 = await service.CachePhotoHashAsync(
            filePath: "/path/to/photo1_renamed.jpg",
            descriptors: descriptors2,
            rows: 7,
            cols: 128,
            fileSize: fileSize,
            lastModifiedUtc: lastMod);

        // Assert: Same record should be updated
        Assert.NotNull(result2);
        Assert.Equal(id1, result2.Id);
        Assert.Equal("/path/to/photo1_renamed.jpg", result2.FilePath);
        Assert.Equal(descriptors2, result2.SiftDescriptors);
        Assert.Equal(7, result2.DescriptorRows);
    }

    [Fact]
    public async Task CacheThumbnail_InsertsNewPhoto_Success()
    {
        // Arrange
        using IPhotoDbService service = new PhotoDbService();
        await service.InitializeAsync(_tempDbPath);
        var fileSize = 4000L;
        var lastMod = DateTime.UtcNow;
        var thumbnail = new byte[] { 255, 0, 255, 0 };

        // Act
        var result = await service.CacheThumbnailAsync(
            fileSize: fileSize,
            lastModifiedUtc: lastMod,
            filePath: "/path/to/photo2.jpg",
            thumbnail: thumbnail);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("/path/to/photo2.jpg", result.FilePath);
        Assert.Equal(fileSize, result.FileSize);
        Assert.Equal(lastMod, result.LastModifiedUtc);
        Assert.Equal(thumbnail, result.Thumbnail);
    }

    [Fact]
    public async Task CacheThumbnail_UpdatesExisting_WhenFingerprintMatches()
    {
        // Arrange
        using IPhotoDbService service = new PhotoDbService();
        await service.InitializeAsync(_tempDbPath);
        var fileSize = 5000L;
        var lastMod = DateTime.UtcNow;
        var thumbnail1 = new byte[] { 1, 1, 1 };
        var thumbnail2 = new byte[] { 2, 2, 2, 2 };

        // Insert first thumbnail
        var result1 = await service.CacheThumbnailAsync(
            fileSize: fileSize,
            lastModifiedUtc: lastMod,
            filePath: "/path/to/photo3.jpg",
            thumbnail: thumbnail1);

        var id1 = result1.Id;

        // Act: Insert thumbnail with same fingerprint
        var result2 = await service.CacheThumbnailAsync(
            fileSize: fileSize,
            lastModifiedUtc: lastMod,
            filePath: "/path/to/photo3_moved.jpg",
            thumbnail: thumbnail2);

        // Assert: Same record should be updated
        Assert.NotNull(result2);
        Assert.Equal(id1, result2.Id);
        Assert.Equal("/path/to/photo3_moved.jpg", result2.FilePath);
        Assert.Equal(thumbnail2, result2.Thumbnail);
    }

    [Fact]
    public async Task GetCachedPhotoAsync_ByFilePath_Works()
    {
        // Arrange
        using IPhotoDbService service = new PhotoDbService();
        await service.InitializeAsync(_tempDbPath);
        var filePath = "/path/to/photo4.jpg";
        var fileSize = 6000L;
        var lastMod = DateTime.UtcNow;

        // Insert photo
        await service.CachePhotoHashAsync(
            filePath: filePath,
            descriptors: [1, 2, 3],
            rows: 10,
            cols: 128,
            fileSize: fileSize,
            lastModifiedUtc: lastMod);

        // Act
        var result = await service.GetCachedPhotoAsync(filePath);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(filePath, result.FilePath);
        Assert.Equal(fileSize, result.FileSize);
    }

    [Fact]
    public async Task StoreSimilarity_NormalizesIds_SmallIdFirst()
    {
        // Arrange
        using IPhotoDbService service = new PhotoDbService();
        await service.InitializeAsync(_tempDbPath);

        // First, create two photos
        var photo1 = await service.CachePhotoHashAsync(
            filePath: "/path/to/photo5a.jpg",
            descriptors: [1],
            rows: 1,
            cols: 128,
            fileSize: 1000,
            lastModifiedUtc: DateTime.UtcNow);

        var photo2 = await service.CachePhotoHashAsync(
            filePath: "/path/to/photo5b.jpg",
            descriptors: [2],
            rows: 1,
            cols: 128,
            fileSize: 2000,
            lastModifiedUtc: DateTime.UtcNow);

        // Act: Store similarity with larger ID first
        await service.StoreSimilarityAsync(
            photo1Id: photo2.Id,
            photo2Id: photo1.Id,
            score: 0.85);

        // Get results
        var results = await service.GetCachedResultsAsync();

        // Assert: IDs should be normalized so smaller ID is first
        Assert.Single(results);
        var similarity = results[0];
        Assert.Equal(photo1.Id, similarity.Photo1Id);
        Assert.Equal(photo2.Id, similarity.Photo2Id);
        Assert.Equal(0.85, similarity.Score);
    }

    [Fact]
    public async Task GetCachedResults_ReturnsOrderedByScore_Ascending()
    {
        // Arrange
        using IPhotoDbService service = new PhotoDbService();
        await service.InitializeAsync(_tempDbPath);

        // Create three photos
        var photos = new List<Photo>();
        for (var i = 0; i < 3; i++)
        {
            var photo = await service.CachePhotoHashAsync(
                filePath: $"/path/to/photo{i}.jpg",
                descriptors: [(byte)i],
                rows: 1,
                cols: 128,
                fileSize: 1000 + i,
                lastModifiedUtc: DateTime.UtcNow);
            photos.Add(photo);
        }

        // Store similarity results in non-sorted order
        await service.StoreSimilarityAsync(photos[0].Id, photos[1].Id, 0.95);
        await service.StoreSimilarityAsync(photos[1].Id, photos[2].Id, 0.65);
        await service.StoreSimilarityAsync(photos[0].Id, photos[2].Id, 0.80);

        // Act
        var results = await service.GetCachedResultsAsync();

        // Assert: Results should be ordered by score ascending
        Assert.Equal(3, results.Count);
        Assert.Equal(0.65, results[0].Score);
        Assert.Equal(0.80, results[1].Score);
        Assert.Equal(0.95, results[2].Score);
    }

    [Fact]
    public async Task AllMethods_ReturnGracefully_WhenNotInitialized()
    {
        // Arrange
        using IPhotoDbService service = new PhotoDbService();
        // Don't call InitializeAsync - service is not available

        // Act & Assert: All methods should return null/empty without throwing

        var photoByFingerprint = await service.GetCachedPhotoByFingerprintAsync(
            fileSize: 1000,
            lastModifiedUtc: DateTime.UtcNow);
        Assert.Null(photoByFingerprint);

        var photoByPath = await service.GetCachedPhotoAsync("/path/to/photo.jpg");
        Assert.Null(photoByPath);

        var cachedHash = await service.CachePhotoHashAsync(
            filePath: "/path/to/photo.jpg",
            descriptors: [1],
            rows: 1,
            cols: 128,
            fileSize: 1000,
            lastModifiedUtc: DateTime.UtcNow);
        Assert.Null(cachedHash);

        var cachedThumbnail = await service.CacheThumbnailAsync(
            fileSize: 1000,
            lastModifiedUtc: DateTime.UtcNow,
            filePath: "/path/to/photo.jpg",
            thumbnail: [1]);
        Assert.Null(cachedThumbnail);

        // StoreSimilarityAsync returns void (no exception expected)
        await service.StoreSimilarityAsync(1, 2, 0.5);

        var results = await service.GetCachedResultsAsync();
        Assert.Empty(results);
    }

    [Fact]
    public async Task Photo_Fingerprint_CombinesSizeAndTimestamp()
    {
        // Arrange
        var fileSize = 7654L;
        var lastMod = new DateTime(2024, 3, 15, 10, 30, 45, DateTimeKind.Utc);

        var photo = new Photo
        {
            FileSize = fileSize,
            LastModifiedUtc = lastMod
        };

        // Act
        var fingerprint = photo.Fingerprint;

        // Assert
        var expectedFingerprint = $"{fileSize}_{lastMod.Ticks}";
        Assert.Equal(expectedFingerprint, fingerprint);
        Assert.Contains("7654", fingerprint);
        Assert.Contains(lastMod.Ticks.ToString(), fingerprint);
    }

    [Fact]
    public async Task CachePhotoHash_RetrieveByFingerprint_VerifyRoundTrip()
    {
        // Arrange
        using IPhotoDbService service = new PhotoDbService();
        await service.InitializeAsync(_tempDbPath);
        var fileSize = 8000L;
        var lastMod = DateTime.UtcNow;
        var filePath = "/path/to/photo_roundtrip.jpg";
        var descriptors = new byte[] { 10, 20, 30, 40, 50 };

        // Act: Cache photo
        await service.CachePhotoHashAsync(
            filePath: filePath,
            descriptors: descriptors,
            rows: 20,
            cols: 128,
            fileSize: fileSize,
            lastModifiedUtc: lastMod);

        // Retrieve by fingerprint
        var retrieved = await service.GetCachedPhotoByFingerprintAsync(fileSize, lastMod);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(filePath, retrieved.FilePath);
        Assert.Equal(descriptors, retrieved.SiftDescriptors);
        Assert.Equal(20, retrieved.DescriptorRows);
        Assert.Equal(128, retrieved.DescriptorCols);
    }
}