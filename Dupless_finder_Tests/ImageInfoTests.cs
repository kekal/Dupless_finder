using System;
using System.IO;
using Dupples_finder_UI;
using Dupples_finder_UI.DTO;
using Moq;
using Prism.Events;
using Xunit;

namespace Dupless_finder_Tests
{
    public class ImageInfoTests : IDisposable
    {
        private string _tempFilePath;
        private readonly string _tempDirPath;
        private readonly IEventAggregator _eventAggregator;

        public ImageInfoTests()
        {
            _tempDirPath = Path.Combine(Path.GetTempPath(), $"ImageInfoTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDirPath);
            _eventAggregator = new Mock<IEventAggregator>().Object;
        }

        private string CreateTempFile(string fileName = "testfile.jpg", long fileSize = 1024)
        {
            _tempFilePath = Path.Combine(_tempDirPath, fileName);

            // Create a file with specific size
            using (var fs = File.Create(_tempFilePath))
            {
                fs.Write(new byte[fileSize], 0, (int)fileSize);
                fs.Flush();
            }

            return _tempFilePath;
        }

        [Fact]
        public void Constructor_SetsFilePath()
        {
            var path = Path.Combine(_tempDirPath, "test.jpg");
            var imageInfo = new ImageInfo(path, _eventAggregator);

            Assert.Equal(path, imageInfo.FilePath);
            imageInfo.Dispose();
        }

        [Fact]
        public void Constructor_SetsFileNameFromPath()
        {
            var path = Path.Combine(_tempDirPath, "myimage.jpg");
            var imageInfo = new ImageInfo(path, _eventAggregator);

            Assert.Equal("myimage.jpg", imageInfo.FileName);
            imageInfo.Dispose();
        }

        [Fact]
        public void Constructor_ReadsFileSize_ForExistingFile()
        {
            var filePath = CreateTempFile("test.jpg", 2048);
            var imageInfo = new ImageInfo(filePath, _eventAggregator);

            Assert.Equal(2048, imageInfo.FileSize);
            imageInfo.Dispose();
        }

        [Fact]
        public void Constructor_ReadsLastModifiedUtc_ForExistingFile()
        {
            var filePath = CreateTempFile("test.jpg");
            var beforeTime = DateTime.UtcNow;

            var imageInfo = new ImageInfo(filePath, _eventAggregator);

            var afterTime = DateTime.UtcNow;
            Assert.True(imageInfo.LastModifiedUtc >= beforeTime.AddSeconds(-1));
            Assert.True(imageInfo.LastModifiedUtc <= afterTime.AddSeconds(1));
            imageInfo.Dispose();
        }

        [Fact]
        public void Constructor_SetsZeroSize_ForNonExistentFile()
        {
            var nonExistentPath = Path.Combine(_tempDirPath, "nonexistent.jpg");
            var imageInfo = new ImageInfo(nonExistentPath, _eventAggregator);

            Assert.Equal(0, imageInfo.FileSize);
            imageInfo.Dispose();
        }

        [Fact]
        public void Fingerprint_Format_IsCorrect()
        {
            var filePath = CreateTempFile("test.jpg", 5120);
            var imageInfo = new ImageInfo(filePath, _eventAggregator);

            var fingerprint = imageInfo.Fingerprint;
            var parts = fingerprint.Split('_');

            Assert.Equal(2, parts.Length);
            Assert.Equal("5120", parts[0]);
            Assert.True(long.TryParse(parts[1], out _), "Second part should be Ticks as long");
            imageInfo.Dispose();
        }

        [Fact]
        public void Fingerprint_DiffersForDifferentFiles()
        {
            var filePath1 = CreateTempFile("test1.jpg");
            var imageInfo1 = new ImageInfo(filePath1, _eventAggregator);

            var filePath2 = Path.Combine(_tempDirPath, "test2.jpg");
            using (var fs = File.Create(filePath2))
            {
                fs.Write(new byte[2048], 0, 2048);
                fs.Flush();
            }
            var imageInfo2 = new ImageInfo(filePath2, _eventAggregator);

            Assert.NotEqual(imageInfo1.Fingerprint, imageInfo2.Fingerprint);
            imageInfo1.Dispose();
            imageInfo2.Dispose();
        }

        [Fact]
        public void IsThumbnailLoaded_FalseByDefault()
        {
            var filePath = CreateTempFile("test.jpg");
            var imageInfo = new ImageInfo(filePath, _eventAggregator);

            Assert.False(imageInfo.IsThumbnailLoaded);
            imageInfo.Dispose();
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirPath))
            {
                try
                {
                    Directory.Delete(_tempDirPath, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }
}
