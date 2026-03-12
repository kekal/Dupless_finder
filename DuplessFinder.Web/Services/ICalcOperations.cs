using DuplessFinder.Web.Models;

namespace DuplessFinder.Web.Services;

public interface ICalcOperations
{
    Task<(Dictionary<string, SiftResult> HashDict, Dictionary<string, string> PathToFingerprint)> CalcSiftHashesAsync(
        IList<ImageInfo> images,
        IProgress<double> progress,
        CancellationToken ct = default);

    Task<List<PairSimilarityInfo>> CreateMatchCollectionAsync(
        Dictionary<string, SiftResult> hashDict,
        Dictionary<string, string> pathToFingerprint,
        IProgress<double> progress,
        CancellationToken ct = default);
}
