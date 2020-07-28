using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace Dupples_finder_UI
{
    public class PairSimilarityInfo : IEquatable<PairSimilarityInfo>
    {
        public readonly KeyValuePair<string, MatOfFloat> Hash1;
        public readonly KeyValuePair<string, MatOfFloat> Hash2;
        public readonly double Match;
        public readonly List<DMatch> MatchPoints;

        public PairSimilarityInfo(KeyValuePair<string, MatOfFloat> hash1, KeyValuePair<string, MatOfFloat> hash2, double match)
        {
            Hash1 = hash1;
            Hash2 = hash2;
            Match = match;
        }

        public bool Equals(PairSimilarityInfo other)
        {
            if (other == null) return false;

            var otherName1 = other.Hash1.Key;
            var otherName2 = other.Hash2.Key;
            var equal = (Hash1.Key == otherName1 && Hash2.Key == otherName2) || (Hash1.Key == otherName2 && Hash2.Key == otherName1);
            return equal;
        }

        public override string ToString()
        {
            return "\n= \n" + Hash1.Key + " \n " + Hash2.Key + "\n has best homogenized feature offset \n" + MatchPoints.FirstOrDefault().Distance + " \n==\n";
        }
    }
}