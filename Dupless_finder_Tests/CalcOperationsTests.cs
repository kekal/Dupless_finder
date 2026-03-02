using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dupples_finder_UI.DTO;
using Dupples_finder_UI.Services;
using Dupples_finder_UI.Services.Interfaces;
using Moq;
using OpenCvSharp;
using Xunit;

namespace Dupless_finder_Tests
{
    public class CalcOperationsTests
    {
        private readonly Mock<IMatSerializer> _mockMatSerializer;
        private readonly Mock<IPhotoDbService> _mockPhotoDbService;
        private readonly CalcOperations _calcOperations;

        public CalcOperationsTests()
        {
            _mockMatSerializer = new Mock<IMatSerializer>();
            _mockPhotoDbService = new Mock<IPhotoDbService>();
            _calcOperations = new CalcOperations(_mockMatSerializer.Object);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithValidMatSerializer_DoesNotThrow()
        {
            // Arrange
            var mockSerializer = new Mock<IMatSerializer>();

            // Act & Assert
            var instance = new CalcOperations(mockSerializer.Object);
            Assert.NotNull(instance);
        }

        [Fact]
        public void Constructor_WithNullMatSerializer_DoesNotThrow()
        {
            // Constructor accepts null; NullReferenceException would only occur
            // when _matSerializer is actually used during SIFT hash computation.
            var instance = new CalcOperations(null);
            Assert.NotNull(instance);
        }

        #endregion

        #region CreateMatchCollection Tests

        [Fact]
        public void CreateMatchCollection_WithEmptyDictionary_ReturnsEmpty()
        {
            // Arrange
            var emptyHashDict = new Dictionary<string, Mat>();
            var progress = new Progress<double>();

            // Act
            var result = _calcOperations.CreateMatchCollection(emptyHashDict, progress);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void CreateMatchCollection_WithSingleEntry_ReturnsEmpty()
        {
            // Arrange
            var singleHashDict = new Dictionary<string, Mat>
            {
                { "image1.jpg", CreateTestMat(128, 128) }
            };
            var progress = new Progress<double>();

            // Act
            var result = _calcOperations.CreateMatchCollection(singleHashDict, progress);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void CreateMatchCollection_WithTwoEntriesButInsufficientDescriptors_ReturnsMaxValueMatch()
        {
            // Arrange
            // Create Mats with less than 2 rows (insufficient for KnnMatch)
            var mat1 = CreateTestMat(1, 128);  // 1 row, 128 cols
            var mat2 = CreateTestMat(1, 128);  // 1 row, 128 cols

            var hashDict = new Dictionary<string, Mat>
            {
                { "image1.jpg", mat1 },
                { "image2.jpg", mat2 }
            };
            var progress = new Progress<double>();

            // Act
            var result = _calcOperations.CreateMatchCollection(hashDict, progress).ToList();

            // Assert — pair is still created, but with MaxValue match (filtered later by UI)
            Assert.Single(result);
            Assert.Equal(double.MaxValue, result[0].Match);

            // Cleanup
            mat1.Release();
            mat2.Release();
        }

        [Fact]
        public void CreateMatchCollection_WithTwoValidEntries_ReturnsMatches()
        {
            // Arrange
            // Create valid SIFT-like descriptors (each row is a feature, 128 cols for SIFT)
            var mat1 = CreateTestMat(5, 128);  // 5 features, 128 dimensions
            var mat2 = CreateTestMat(5, 128);  // 5 features, 128 dimensions

            var hashDict = new Dictionary<string, Mat>
            {
                { "image1.jpg", mat1 },
                { "image2.jpg", mat2 }
            };
            var progress = new Progress<double>();

            // Act
            var result = _calcOperations.CreateMatchCollection(hashDict, progress);

            // Assert
            Assert.NotEmpty(result);
            // With 2 entries, we expect exactly 1 pair (2*(2-1)/2 = 1)
            Assert.Single(result);
            var pairInfo = result.First();
            Assert.Equal("image1.jpg", pairInfo.Hash1.Key);
            Assert.Equal("image2.jpg", pairInfo.Hash2.Key);

            // Cleanup
            mat1.Release();
            mat2.Release();
        }

        [Fact]
        public void CreateMatchCollection_WithThreeValidEntries_ReturnsThreeMatches()
        {
            // Arrange
            var mat1 = CreateTestMat(5, 128);
            var mat2 = CreateTestMat(5, 128);
            var mat3 = CreateTestMat(5, 128);

            var hashDict = new Dictionary<string, Mat>
            {
                { "image1.jpg", mat1 },
                { "image2.jpg", mat2 },
                { "image3.jpg", mat3 }
            };
            var progress = new Progress<double>();

            // Act
            var result = _calcOperations.CreateMatchCollection(hashDict, progress);

            // Assert
            // With 3 entries, we expect 3 pairs: (1,2), (1,3), (2,3)
            Assert.Equal(3, result.Count());

            // Cleanup
            mat1.Release();
            mat2.Release();
            mat3.Release();
        }

        [Fact]
        public void CreateMatchCollection_WithMismatchedDescriptorColumns_ReturnsMaxValueMatch()
        {
            // Arrange
            var mat1 = CreateTestMat(5, 128);
            var mat2 = CreateTestMat(5, 64);   // Different column count

            var hashDict = new Dictionary<string, Mat>
            {
                { "image1.jpg", mat1 },
                { "image2.jpg", mat2 }
            };
            var progress = new Progress<double>();

            // Act
            var result = _calcOperations.CreateMatchCollection(hashDict, progress).ToList();

            // Assert — pair is still created, but with MaxValue match (filtered later by UI)
            Assert.Single(result);
            Assert.Equal(double.MaxValue, result[0].Match);

            // Cleanup
            mat1.Release();
            mat2.Release();
        }

        [Fact]
        public void CreateMatchCollection_WithNullProgress_DoesNotThrow()
        {
            // Arrange
            var mat1 = CreateTestMat(5, 128);
            var mat2 = CreateTestMat(5, 128);

            var hashDict = new Dictionary<string, Mat>
            {
                { "image1.jpg", mat1 },
                { "image2.jpg", mat2 }
            };

            // Act & Assert
            var result = _calcOperations.CreateMatchCollection(hashDict, null);
            Assert.NotEmpty(result);

            // Cleanup
            mat1.Release();
            mat2.Release();
        }

        [Fact]
        public void CreateMatchCollection_ResultsAreOrderedByMatch()
        {
            // Arrange
            var mat1 = CreateTestMat(5, 128);
            var mat2 = CreateTestMat(5, 128);
            var mat3 = CreateTestMat(5, 128);

            var hashDict = new Dictionary<string, Mat>
            {
                { "image1.jpg", mat1 },
                { "image2.jpg", mat2 },
                { "image3.jpg", mat3 }
            };
            var progress = new Progress<double>();

            // Act
            var result = _calcOperations.CreateMatchCollection(hashDict, progress).ToList();

            // Assert
            // Results should be ordered by Match value (lowest first)
            for (int i = 0; i < result.Count - 1; i++)
            {
                Assert.True(result[i].Match <= result[i + 1].Match);
            }

            // Cleanup
            mat1.Release();
            mat2.Release();
            mat3.Release();
        }

        #endregion

        #region CalcSiftHashes Tests

        [Fact]
        public async Task CalcSiftHashes_WithEmptyImageList_ReturnsEmptyDictionary()
        {
            // Arrange
            var emptyImageList = new List<ImageInfo>();
            var progress = new Progress<double>();

            // Act
            var result = _calcOperations.CalcSiftHashes(emptyImageList, _mockPhotoDbService.Object, progress, out Task resultTask);

            // Assert
            Assert.Empty(result);
            Assert.NotNull(resultTask);
            await resultTask; // Ensure task completes without error
        }

        [Fact]
        public async Task CalcSiftHashes_ReturnsTaskOutParameter()
        {
            // Arrange
            var imageList = new List<ImageInfo>();
            var progress = new Progress<double>();

            // Act
            var result = _calcOperations.CalcSiftHashes(imageList, _mockPhotoDbService.Object, progress, out Task resultTask);

            // Assert
            Assert.NotNull(resultTask);
            Assert.IsAssignableFrom<Task>(resultTask);
            await resultTask;
        }

        [Fact]
        public async Task CalcSiftHashes_WithNullProgress_DoesNotThrow()
        {
            // Arrange
            var imageList = new List<ImageInfo>();

            // Act & Assert
            var result = _calcOperations.CalcSiftHashes(imageList, _mockPhotoDbService.Object, null, out Task resultTask);
            Assert.NotNull(result);
            await resultTask;
        }

        [Fact]
        public async Task CalcSiftHashes_WithNullDbService_ReturnsValidDictionary()
        {
            // Arrange
            var imageList = new List<ImageInfo>();
            var progress = new Progress<double>();

            // Act
            var result = _calcOperations.CalcSiftHashes(imageList, null, progress, out Task resultTask);

            // Assert
            Assert.IsType<ConcurrentDictionary<string, Mat>>(result);
            await resultTask;
        }

        [Fact]
        public async Task CalcSiftHashes_WithDefaultThumbSize_UsesDefaultValue()
        {
            // Arrange
            var imageList = new List<ImageInfo>();
            var progress = new Progress<double>();

            // Act - uses default thumbSize of 256
            var result = _calcOperations.CalcSiftHashes(imageList, _mockPhotoDbService.Object, progress, out Task resultTask);

            // Assert
            Assert.NotNull(result);
            await resultTask;
        }

        [Fact]
        public async Task CalcSiftHashes_WithCustomThumbSize_AcceptsParameter()
        {
            // Arrange
            var imageList = new List<ImageInfo>();
            var progress = new Progress<double>();
            int customThumbSize = 512;

            // Act
            var result = _calcOperations.CalcSiftHashes(imageList, _mockPhotoDbService.Object, progress, out Task resultTask, customThumbSize);

            // Assert
            Assert.NotNull(result);
            await resultTask;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Creates a test Mat with the specified dimensions filled with random float values.
        /// Used to simulate SIFT descriptor matrices.
        /// </summary>
        private Mat CreateTestMat(int rows, int cols)
        {
            var mat = new Mat(rows, cols, MatType.CV_32F);
            var rng = new Random();
            var data = new float[rows * cols];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (float)rng.NextDouble();
            }
            System.Runtime.InteropServices.Marshal.Copy(data, 0, mat.Data, data.Length);
            return mat;
        }

        #endregion
    }
}
