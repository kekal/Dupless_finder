namespace DuplessFinder.Web.Models;

/// <summary>
/// Represents a pair of duplicate images for display in the pair grid.
/// Does not own Image1/Image2 — they are shared references.
/// </summary>
public class ImagePair
{
    public ImageInfo? Image1 { get; set; }
    public ImageInfo? Image2 { get; set; }
    public double Score { get; set; }
    public string ScoreDisplay => Score.ToString("F1");
}
