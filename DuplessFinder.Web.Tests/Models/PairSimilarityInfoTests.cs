using DuplessFinder.Web.Models;
using FluentAssertions;
using Xunit;

namespace DuplessFinder.Web.Tests.Models;

public class PairSimilarityInfoTests
{
    [Fact]
    public void Constructor_SetsFilePath1()
    {
        var pair = new PairSimilarityInfo("path/a.jpg", "path/b.jpg", "fp1", "fp2", 85.5);

        pair.FilePath1.Should().Be("path/a.jpg");
    }

    [Fact]
    public void Constructor_SetsFilePath2()
    {
        var pair = new PairSimilarityInfo("path/a.jpg", "path/b.jpg", "fp1", "fp2", 85.5);

        pair.FilePath2.Should().Be("path/b.jpg");
    }

    [Fact]
    public void Constructor_SetsFingerprint1()
    {
        var pair = new PairSimilarityInfo("path/a.jpg", "path/b.jpg", "fp1", "fp2", 85.5);

        pair.Fingerprint1.Should().Be("fp1");
    }

    [Fact]
    public void Constructor_SetsFingerprint2()
    {
        var pair = new PairSimilarityInfo("path/a.jpg", "path/b.jpg", "fp1", "fp2", 85.5);

        pair.Fingerprint2.Should().Be("fp2");
    }

    [Fact]
    public void Constructor_SetsScore()
    {
        var pair = new PairSimilarityInfo("path/a.jpg", "path/b.jpg", "fp1", "fp2", 85.5);

        pair.Score.Should().Be(85.5);
    }

    #region Equality Tests

    [Fact]
    public void Equals_ReturnsTrueForSamePathsInSameOrder()
    {
        var pair1 = new PairSimilarityInfo("image1.jpg", "image2.jpg", "fp1", "fp2", 90);
        var pair2 = new PairSimilarityInfo("image1.jpg", "image2.jpg", "fp1", "fp2", 90);

        pair1.Equals(pair2).Should().BeTrue();
    }

    [Fact]
    public void Equals_ReturnsTrueForSamePathsInReversedOrder()
    {
        var pair1 = new PairSimilarityInfo("image1.jpg", "image2.jpg", "fp1", "fp2", 90);
        var pair2 = new PairSimilarityInfo("image2.jpg", "image1.jpg", "fp2", "fp1", 90);

        pair1.Equals(pair2).Should().BeTrue();
    }

    [Fact]
    public void Equals_ReturnsFalseForDifferentPaths()
    {
        var pair1 = new PairSimilarityInfo("image1.jpg", "image2.jpg", "fp1", "fp2", 90);
        var pair2 = new PairSimilarityInfo("image1.jpg", "image3.jpg", "fp1", "fp3", 90);

        pair1.Equals(pair2).Should().BeFalse();
    }

    [Fact]
    public void Equals_ReturnsFalseForNull()
    {
        var pair = new PairSimilarityInfo("image1.jpg", "image2.jpg", "fp1", "fp2", 90);

        pair.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void Equals_ReturnsFalseForDifferentType()
    {
        var pair = new PairSimilarityInfo("image1.jpg", "image2.jpg", "fp1", "fp2", 90);
        var other = new object();

        pair.Equals(other).Should().BeFalse();
    }

    #endregion

    #region GetHashCode Tests

    [Fact]
    public void GetHashCode_SameForABAndBA()
    {
        var pairAB = new PairSimilarityInfo("image1.jpg", "image2.jpg", "fp1", "fp2", 90);
        var pairBA = new PairSimilarityInfo("image2.jpg", "image1.jpg", "fp2", "fp1", 90);

        pairAB.GetHashCode().Should().Be(pairBA.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentForDifferentPairs()
    {
        var pair1 = new PairSimilarityInfo("image1.jpg", "image2.jpg", "fp1", "fp2", 90);
        var pair2 = new PairSimilarityInfo("image1.jpg", "image3.jpg", "fp1", "fp3", 90);

        pair1.GetHashCode().Should().NotBe(pair2.GetHashCode());
    }

    #endregion

    #region HashSet Integration Tests

    [Fact]
    public void CanBeUsedInHashSet_DeduplicatesByOrderIndependentEquality()
    {
        var set = new HashSet<PairSimilarityInfo>
        {
            new("image1.jpg", "image2.jpg", "fp1", "fp2", 90),
            new("image2.jpg", "image1.jpg", "fp2", "fp1", 90), // Same pair, reversed
            new("image1.jpg", "image3.jpg", "fp1", "fp3", 85)   // Different pair
        };

        set.Count.Should().Be(2); // First two are considered the same
    }

    [Fact]
    public void HashSet_CanPerformMembershipTest()
    {
        var set = new HashSet<PairSimilarityInfo>
        {
            new("image1.jpg", "image2.jpg", "fp1", "fp2", 90)
        };

        var reversedPair = new PairSimilarityInfo("image2.jpg", "image1.jpg", "fp2", "fp1", 90);

        set.Contains(reversedPair).Should().BeTrue();
    }

    [Fact]
    public void HashSet_PreventsDuplicateAddition()
    {
        var set = new HashSet<PairSimilarityInfo>();
        var pair1 = new PairSimilarityInfo("image1.jpg", "image2.jpg", "fp1", "fp2", 90);
        var pair2 = new PairSimilarityInfo("image2.jpg", "image1.jpg", "fp2", "fp1", 90);

        var added1 = set.Add(pair1);
        var added2 = set.Add(pair2);

        added1.Should().BeTrue();
        added2.Should().BeFalse();
        set.Count.Should().Be(1);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Equals_WithSamePathForBothFiles()
    {
        var pair1 = new PairSimilarityInfo("image.jpg", "image.jpg", "fp", "fp", 0);
        var pair2 = new PairSimilarityInfo("image.jpg", "image.jpg", "fp", "fp", 0);

        pair1.Equals(pair2).Should().BeTrue();
    }

    [Fact]
    public void GetHashCode_Consistent_AcrossMultipleCalls()
    {
        var pair = new PairSimilarityInfo("image1.jpg", "image2.jpg", "fp1", "fp2", 90);

        var hash1 = pair.GetHashCode();
        var hash2 = pair.GetHashCode();

        hash1.Should().Be(hash2);
    }

    #endregion
}
