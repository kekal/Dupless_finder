using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace Dupples_finder_UI.DTO
{
    public class PairSimilarityInfo : IEquatable<PairSimilarityInfo>
    {
        public readonly KeyValuePair<string, Mat> Hash1;
        public readonly KeyValuePair<string, Mat> Hash2;
        public readonly double Match;

        public PairSimilarityInfo(KeyValuePair<string, Mat> hash1, KeyValuePair<string, Mat> hash2, double match)
        {
            Hash1 = hash1;
            Hash2 = hash2;
            Match = match;
        }

        public bool Equals(PairSimilarityInfo other)
        {
            if (other == null)
            {
                return false;
            }

            var otherName1 = other.Hash1.Key;
            var otherName2 = other.Hash2.Key;
            var equal = (Hash1.Key == otherName1 && Hash2.Key == otherName2) || (Hash1.Key == otherName2 && Hash2.Key == otherName1);
            return equal;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as PairSimilarityInfo);
        }

        public override int GetHashCode()
        {
            // Order-independent hash so (A,B) and (B,A) produce the same code
            int h1 = Hash1.Key?.GetHashCode() ?? 0;
            int h2 = Hash2.Key?.GetHashCode() ?? 0;
            return h1 ^ h2;
        }

        public override string ToString()
        {
            return "\n= \n" + Hash1.Key + " \n " + Hash2.Key + "\n Match: " + Match.ToString("F") + " \n==\n";
        }
    }
}
