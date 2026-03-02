using System;
using System.IO;
using System.Linq;
using Dupples_finder_UI.Data;
using Dupples_finder_UI.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Dupless_finder_Tests
{
    /// <summary>
    /// xUnit tests for the DuplessDbContext EF Core data layer.
    /// Tests schema creation, entity insertion, uniqueness constraints, and foreign key relationships.
    /// Uses temporary SQLite database files for test isolation.
    /// </summary>
    public class DuplessDbContextTests : IDisposable
    {
        private readonly string _tempDbPath;

        public DuplessDbContextTests()
        {
            _tempDbPath = Path.Combine(Path.GetTempPath(), $"test_dupless_{Guid.NewGuid()}.db");
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

        #region Schema Creation Tests

        [Fact]
        public void EnsureCreated_CreatesSchema()
        {
            // Arrange
            using var context = new DuplessDbContext(_tempDbPath);
            // Act
            bool created = context.Database.EnsureCreated();

            // Assert
            Assert.True(created, "Database should be created on first call");
            Assert.True(File.Exists(_tempDbPath), "Database file should exist");
        }

        [Fact]
        public void EnsureCreated_Idempotent()
        {
            // Arrange
            using var context = new DuplessDbContext(_tempDbPath);
            // Act
            bool created1 = context.Database.EnsureCreated();
            bool created2 = context.Database.EnsureCreated();

            // Assert
            Assert.True(created1, "First call should create database");
            Assert.False(created2, "Second call should not create again (idempotent)");
        }

        #endregion

        #region Photo Entity Tests

        [Fact]
        public void Photos_CanInsertAndRetrieve()
        {
            // Arrange
            var photo = new Photo
            {
                FilePath = "/path/to/photo1.jpg",
                FileSize = 1024000,
                LastModifiedUtc = DateTime.UtcNow,
                HashDate = DateTime.UtcNow,
                SiftDescriptors = [1, 2, 3, 4, 5],
                DescriptorRows = 10,
                DescriptorCols = 128
            };

            using (var context = new DuplessDbContext(_tempDbPath))
            {
                context.Database.EnsureCreated();

                // Act
                context.Photos.Add(photo);
                context.SaveChanges();
            }

            // Assert
            using (var context = new DuplessDbContext(_tempDbPath))
            {
                var retrieved = context.Photos.FirstOrDefault(p => p.FilePath == "/path/to/photo1.jpg");
                Assert.NotNull(retrieved);
                Assert.Equal("/path/to/photo1.jpg", retrieved.FilePath);
                Assert.Equal(1024000, retrieved.FileSize);
                Assert.Equal([1, 2, 3, 4, 5], retrieved.SiftDescriptors);
                Assert.Equal(10, retrieved.DescriptorRows);
                Assert.Equal(128, retrieved.DescriptorCols);
            }
        }

        [Fact]
        public void Photos_EnforceFingerprintUniqueness()
        {
            // Arrange
            var now = DateTime.UtcNow;
            var photo1 = new Photo
            {
                FilePath = "/path/to/photo1.jpg",
                FileSize = 2000000,
                LastModifiedUtc = now,
                HashDate = DateTime.UtcNow,
                DescriptorRows = 0,
                DescriptorCols = 128
            };

            var photo2 = new Photo
            {
                FilePath = "/path/to/photo2.jpg",
                FileSize = 2000000,  // Same size
                LastModifiedUtc = now,  // Same modification time
                HashDate = DateTime.UtcNow,
                DescriptorRows = 0,
                DescriptorCols = 128
            };

            using var context = new DuplessDbContext(_tempDbPath);
            context.Database.EnsureCreated();

            // Act & Assert
            context.Photos.Add(photo1);
            context.SaveChanges();

            context.Photos.Add(photo2);

            // Should throw on second SaveChanges due to unique constraint on (FileSize, LastModifiedUtc)
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        [Fact]
        public void Photos_AllowDuplicateFilePaths()
        {
            // Arrange
            var now1 = DateTime.UtcNow;
            var now2 = now1.AddSeconds(1);

            var photo1 = new Photo
            {
                FilePath = "/path/to/photo.jpg",
                FileSize = 3000000,
                LastModifiedUtc = now1,
                HashDate = DateTime.UtcNow,
                DescriptorRows = 0,
                DescriptorCols = 128
            };

            var photo2 = new Photo
            {
                FilePath = "/path/to/photo.jpg",  // Same path but different fingerprint
                FileSize = 3000001,  // Different size
                LastModifiedUtc = now2,  // Different time
                HashDate = DateTime.UtcNow,
                DescriptorRows = 0,
                DescriptorCols = 128
            };

            using var context = new DuplessDbContext(_tempDbPath);
            context.Database.EnsureCreated();

            // Act
            context.Photos.Add(photo1);
            context.Photos.Add(photo2);

            // Assert: Should succeed because they have different fingerprints
            context.SaveChanges();
            Assert.Equal(2, context.Photos.Count());
        }

        [Fact]
        public void Photo_Fingerprint_IsIgnored_NotInDb()
        {
            // Arrange & Act
            var photo = new Photo
            {
                FilePath = "/path/to/photo.jpg",
                FileSize = 4000000,
                LastModifiedUtc = new DateTime(2024, 3, 15, 10, 30, 45, DateTimeKind.Utc),
                HashDate = DateTime.UtcNow,
                DescriptorRows = 0,
                DescriptorCols = 128
            };

            using (var context = new DuplessDbContext(_tempDbPath))
            {
                context.Database.EnsureCreated();
                context.Photos.Add(photo);
                context.SaveChanges();
            }

            // Assert: Verify fingerprint is computed in-memory, not in database
            using (var context = new DuplessDbContext(_tempDbPath))
            {
                var retrieved = context.Photos.FirstOrDefault();
                Assert.NotNull(retrieved);

                // Fingerprint is computed from FileSize and LastModifiedUtc
                var expectedFingerprint = $"{retrieved.FileSize}_{retrieved.LastModifiedUtc.Ticks}";
                Assert.Equal(expectedFingerprint, retrieved.Fingerprint);

                // Verify the computed fingerprint is correct
                Assert.Contains("4000000", retrieved.Fingerprint);
                Assert.Contains(new DateTime(2024, 3, 15, 10, 30, 45, DateTimeKind.Utc).Ticks.ToString(), retrieved.Fingerprint);
            }
        }

        #endregion

        #region SimilarityResult Entity Tests

        [Fact]
        public void SimilarityResults_CanInsertWithForeignKeys()
        {
            // Arrange
            var photo1 = new Photo
            {
                FilePath = "/path/to/photo1.jpg",
                FileSize = 5000000,
                LastModifiedUtc = DateTime.UtcNow,
                HashDate = DateTime.UtcNow,
                DescriptorRows = 0,
                DescriptorCols = 128
            };

            var photo2 = new Photo
            {
                FilePath = "/path/to/photo2.jpg",
                FileSize = 6000000,
                LastModifiedUtc = DateTime.UtcNow.AddSeconds(1),
                HashDate = DateTime.UtcNow,
                DescriptorRows = 0,
                DescriptorCols = 128
            };

            using var context = new DuplessDbContext(_tempDbPath);
            context.Database.EnsureCreated();

            // Act: Insert photos first
            context.Photos.Add(photo1);
            context.Photos.Add(photo2);
            context.SaveChanges();

            // Get the IDs
            var id1 = photo1.Id;
            var id2 = photo2.Id;

            // Insert similarity result
            var result = new SimilarityResult
            {
                Photo1Id = id1,
                Photo2Id = id2,
                Score = 0.85,
                CompareDate = DateTime.UtcNow
            };

            context.SimilarityResults.Add(result);
            context.SaveChanges();

            // Assert
            var retrieved = context.SimilarityResults.FirstOrDefault();
            Assert.NotNull(retrieved);
            Assert.Equal(id1, retrieved.Photo1Id);
            Assert.Equal(id2, retrieved.Photo2Id);
            Assert.Equal(0.85, retrieved.Score);
        }

        [Fact]
        public void SimilarityResults_EnforcesCompositeUniqueness()
        {
            // Arrange: Create two photos
            var photo1 = new Photo
            {
                FilePath = "/path/to/photo1.jpg",
                FileSize = 7000000,
                LastModifiedUtc = DateTime.UtcNow,
                HashDate = DateTime.UtcNow,
                DescriptorRows = 0,
                DescriptorCols = 128
            };

            var photo2 = new Photo
            {
                FilePath = "/path/to/photo2.jpg",
                FileSize = 8000000,
                LastModifiedUtc = DateTime.UtcNow.AddSeconds(1),
                HashDate = DateTime.UtcNow,
                DescriptorRows = 0,
                DescriptorCols = 128
            };

            using var context = new DuplessDbContext(_tempDbPath);
            context.Database.EnsureCreated();

            // Insert photos
            context.Photos.Add(photo1);
            context.Photos.Add(photo2);
            context.SaveChanges();

            var id1 = photo1.Id;
            var id2 = photo2.Id;

            // Insert first similarity result
            var result1 = new SimilarityResult
            {
                Photo1Id = id1,
                Photo2Id = id2,
                Score = 0.90,
                CompareDate = DateTime.UtcNow
            };

            context.SimilarityResults.Add(result1);
            context.SaveChanges();

            // Act & Assert: Try to insert duplicate
            var result2 = new SimilarityResult
            {
                Photo1Id = id1,
                Photo2Id = id2,
                Score = 0.92,  // Different score, but same photo pair
                CompareDate = DateTime.UtcNow
            };

            context.SimilarityResults.Add(result2);

            // Should throw on second SaveChanges due to unique constraint on (Photo1Id, Photo2Id)
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        [Fact]
        public void SimilarityResults_AllowDifferentPhotoPairs()
        {
            // Arrange: Create three photos
            var photos = new[]
            {
                new Photo
                {
                    FilePath = "/path/to/photo1.jpg",
                    FileSize = 9000000,
                    LastModifiedUtc = DateTime.UtcNow,
                    HashDate = DateTime.UtcNow,
                    DescriptorRows = 0,
                    DescriptorCols = 128
                },
                new Photo
                {
                    FilePath = "/path/to/photo2.jpg",
                    FileSize = 10000000,
                    LastModifiedUtc = DateTime.UtcNow.AddSeconds(1),
                    HashDate = DateTime.UtcNow,
                    DescriptorRows = 0,
                    DescriptorCols = 128
                },
                new Photo
                {
                    FilePath = "/path/to/photo3.jpg",
                    FileSize = 11000000,
                    LastModifiedUtc = DateTime.UtcNow.AddSeconds(2),
                    HashDate = DateTime.UtcNow,
                    DescriptorRows = 0,
                    DescriptorCols = 128
                }
            };

            using var context = new DuplessDbContext(_tempDbPath);
            context.Database.EnsureCreated();

            // Insert photos
            context.Photos.AddRange(photos);
            context.SaveChanges();

            // Act: Insert multiple different similarity results
            context.SimilarityResults.Add(new SimilarityResult
            {
                Photo1Id = photos[0].Id,
                Photo2Id = photos[1].Id,
                Score = 0.85,
                CompareDate = DateTime.UtcNow
            });

            context.SimilarityResults.Add(new SimilarityResult
            {
                Photo1Id = photos[1].Id,
                Photo2Id = photos[2].Id,
                Score = 0.75,
                CompareDate = DateTime.UtcNow
            });

            context.SimilarityResults.Add(new SimilarityResult
            {
                Photo1Id = photos[0].Id,
                Photo2Id = photos[2].Id,
                Score = 0.95,
                CompareDate = DateTime.UtcNow
            });

            context.SaveChanges();

            // Assert
            Assert.Equal(3, context.SimilarityResults.Count());
        }

        #endregion
    }
}
