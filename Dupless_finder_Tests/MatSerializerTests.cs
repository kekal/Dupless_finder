using Dupples_finder_UI.Services;
using OpenCvSharp;
using Xunit;

namespace Dupless_finder_Tests;

/// <summary>
/// xUnit tests for the MatSerializer utility class.
/// </summary>
public class MatSerializerTests
{
    private readonly MatSerializer _serializer = new();

    [Fact]
    public void Serialize_Deserialize_RoundTrip_Success()
    {
        // Arrange: Create a Mat with known values
        using var originalMat = new Mat(3, 4, MatType.CV_32FC1);
        // Act: Serialize
        var serialized = _serializer.Serialize(originalMat);

        // Assert serialization succeeded
        Assert.NotNull(serialized);
        Assert.NotEmpty(serialized);
        // Expected size: 3 rows * 4 cols * 4 bytes per float = 48 bytes
        Assert.Equal(3 * 4 * sizeof(float), serialized.Length);

        // Act: Deserialize
        using var deserializedMat = _serializer.Deserialize(serialized, 3, 4);
        // Assert deserialization succeeded
        Assert.NotNull(deserializedMat);
        Assert.Equal(3, deserializedMat.Rows);
        Assert.Equal(4, deserializedMat.Cols);
        Assert.Equal(MatType.CV_32FC1, deserializedMat.Type());
        // Verify the deserialized mat has the same data size
        Assert.Equal(originalMat.Total() * originalMat.ElemSize(),
            deserializedMat.Total() * deserializedMat.ElemSize());
    }

    [Fact]
    public void Deserialize_ReturnsNull_OnEmptyBytes()
    {
        // Arrange
        byte[] emptyBytes = [];

        // Act
        var result = _serializer.Deserialize(emptyBytes, 0, 0);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Deserialize_ReturnsNull_OnNullBytes()
    {
        // Arrange
        byte[] nullBytes = null;

        // Act
        var result = _serializer.Deserialize(nullBytes, 0, 0);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Serialize_ReturnsNull_OnNullMat()
    {
        // Arrange
        Mat nullMat = null;

        // Act
        var result = _serializer.Serialize(nullMat);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Serialize_ReturnsNull_OnEmptyMat()
    {
        // Arrange
        using var emptyMat = new Mat();
        // Act
        var result = _serializer.Serialize(emptyMat);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Serialize_LargeMat_CorrectSize()
    {
        // Arrange: Create a larger Mat
        using var originalMat = new Mat(100, 100, MatType.CV_32FC1);
        // Act: Serialize
        var serialized = _serializer.Serialize(originalMat);

        // Assert serialization succeeded with correct size
        Assert.NotNull(serialized);
        Assert.Equal(100 * 100 * sizeof(float), serialized.Length);
    }

    [Fact]
    public void Deserialize_LargeMat_Success()
    {
        // Arrange: Create and serialize a larger Mat
        using var originalMat = new Mat(50, 50, MatType.CV_32FC1);
        var serialized = _serializer.Serialize(originalMat);

        // Act: Deserialize
        using var deserializedMat = _serializer.Deserialize(serialized, 50, 50);
        // Assert deserialization succeeded
        Assert.NotNull(deserializedMat);
        Assert.Equal(50, deserializedMat.Rows);
        Assert.Equal(50, deserializedMat.Cols);
        Assert.Equal(50 * 50 * sizeof(float),
            deserializedMat.Total() * deserializedMat.ElemSize());
    }

    [Fact]
    public void Serialize_Deserialize_VaryingDimensions_Success()
    {
        // Test multiple dimension combinations
        var dimensions = new int[][]
        {
            [1, 1],
            [10, 1],
            [1, 128],
            [5, 20],
            [32, 64]
        };

        foreach (var dims in dimensions)
        {
            // Arrange
            using var originalMat = new Mat(dims[0], dims[1], MatType.CV_32FC1);
            // Act: Serialize
            var serialized = _serializer.Serialize(originalMat);

            // Assert serialization
            Assert.NotNull(serialized);
            Assert.Equal(dims[0] * dims[1] * sizeof(float), serialized.Length);

            // Act: Deserialize
            using var deserializedMat = _serializer.Deserialize(serialized, dims[0], dims[1]);
            // Assert deserialization
            Assert.NotNull(deserializedMat);
            Assert.Equal(dims[0], deserializedMat.Rows);
            Assert.Equal(dims[1], deserializedMat.Cols);
        }
    }
}