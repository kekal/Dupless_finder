using DuplessFinder.Web.Models;
using DuplessFinder.Web.Services;
using FluentAssertions;
using Xunit;

namespace DuplessFinder.Web.Tests.Models;

public class ImagePairTests
{
    private static ImageInfo CreateImageInfo(string path = "test/image.jpg", string name = "image.jpg", long size = 1000, long lastModified = 12345)
    {
        var entry = new FileEntry(path, name, size, lastModified);
        return new ImageInfo(entry);
    }

    [Fact]
    public void Image1_DefaultsToNull()
    {
        var pair = new ImagePair();

        pair.Image1.Should().BeNull();
    }

    [Fact]
    public void Image2_DefaultsToNull()
    {
        var pair = new ImagePair();

        pair.Image2.Should().BeNull();
    }

    [Fact]
    public void Score_DefaultsToZero()
    {
        var pair = new ImagePair();

        pair.Score.Should().Be(0);
    }

    [Fact]
    public void ScoreDisplay_FormatsWithOneDecimalPlace()
    {
        var pair = new ImagePair { Score = 123.456 };

        pair.ScoreDisplay.Should().Be("123.5");
    }

    [Fact]
    public void ScoreDisplay_WithZero_Returns_0_0()
    {
        var pair = new ImagePair { Score = 0 };

        pair.ScoreDisplay.Should().Be("0.0");
    }

    [Fact]
    public void ScoreDisplay_WithInteger_DisplaysWithDecimalPoint()
    {
        var pair = new ImagePair { Score = 95 };

        pair.ScoreDisplay.Should().Be("95.0");
    }

    [Fact]
    public void Setting_Image1_Works()
    {
        var image = CreateImageInfo();
        var pair = new ImagePair();

        pair.Image1 = image;

        pair.Image1.Should().Be(image);
    }

    [Fact]
    public void Setting_Image2_Works()
    {
        var image = CreateImageInfo("test/image2.jpg");
        var pair = new ImagePair();

        pair.Image2 = image;

        pair.Image2.Should().Be(image);
    }

    [Fact]
    public void Setting_Score_Works()
    {
        var pair = new ImagePair();

        pair.Score = 85.75;

        pair.Score.Should().Be(85.75);
    }

    [Fact]
    public void Can_Set_AllPropertiesAtOnce()
    {
        var image1 = CreateImageInfo("path1.jpg");
        var image2 = CreateImageInfo("path2.jpg");
        var score = 92.5;

        var pair = new ImagePair
        {
            Image1 = image1,
            Image2 = image2,
            Score = score
        };

        pair.Image1.Should().Be(image1);
        pair.Image2.Should().Be(image2);
        pair.Score.Should().Be(score);
        pair.ScoreDisplay.Should().Be("92.5");
    }

    [Theory]
    [InlineData(0.0, "0.0")]
    [InlineData(12.34, "12.3")]
    [InlineData(99.99, "100.0")]
    [InlineData(50.05, "50.0")]
    public void ScoreDisplay_VariousScores_FormatsCorrectly(double score, string expected)
    {
        var pair = new ImagePair { Score = score };

        pair.ScoreDisplay.Should().Be(expected);
    }
}
