namespace DuplessFinder.Web.Models;

/// <summary>
/// Represents a pair of images with their similarity score.
/// Order-independent equality: (A,B) == (B,A).
/// </summary>
public class PairSimilarityInfo : IEquatable<PairSimilarityInfo>
{
    public string FilePath1 { get; }
    public string FilePath2 { get; }
    public string Fingerprint1 { get; }
    public string Fingerprint2 { get; }
    public double Score { get; }

    public PairSimilarityInfo(string filePath1, string filePath2, string fingerprint1, string fingerprint2, double score)
    {
        FilePath1 = filePath1;
        FilePath2 = filePath2;
        Fingerprint1 = fingerprint1;
        Fingerprint2 = fingerprint2;
        Score = score;
    }

    public override bool Equals(object? obj) => Equals(obj as PairSimilarityInfo);

    public override int GetHashCode()
    {
        var h1 = FilePath1.GetHashCode();
        var h2 = FilePath2.GetHashCode();
        (h1, h2) = h1 < h2 ? (h1, h2) : (h2, h1);
        return HashCode.Combine(h1, h2);
    }

    public bool Equals(PairSimilarityInfo? other)
    {
        if (other is null) return false;
        return (FilePath1 == other.FilePath1 && FilePath2 == other.FilePath2)
            || (FilePath1 == other.FilePath2 && FilePath2 == other.FilePath1);
    }
}
