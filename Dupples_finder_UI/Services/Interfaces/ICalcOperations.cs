using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dupples_finder_UI.DTO;
using OpenCvSharp;

namespace Dupples_finder_UI.Services.Interfaces;

public interface ICalcOperations
{
    ConcurrentDictionary<string, Mat> CalcSiftHashes(
        IEnumerable<ImageInfo> infos,
        IPhotoDbService dbService,
        IProgress<double> progress,
        out Task result,
        int thumbSize = 256,
        CancellationToken ct = default);

    IEnumerable<PairSimilarityInfo> CreateMatchCollection(
        IDictionary<string, Mat> hashDict,
        IProgress<double> progress,
        CancellationToken ct = default);
}