using System;

namespace Dupples_finder_UI
{
    public class PairSimilarityInfo : IEquatable<PairSimilarityInfo>
    {
        public readonly string Name1;
        public readonly string Name2;
        public readonly double Match;
        public readonly float[] MatchPoints;

        public PairSimilarityInfo(string name1, string name2, double match)
        {
            Name1 = name1;
            Name2 = name2;
            Match = match;
            MatchPoints = matchPoints;
        }

        public bool Equals(PairSimilarityInfo other)
        {
            if (other == null) return false;

            var otherName1 = other.Name1;
            var otherName2 = other.Name2;
            var equal = (Name1 == otherName1 && Name2 == otherName2) || (Name1 == otherName2 && Name2 == otherName1);
            return equal;
        }

        public override string ToString()
        {
            return "\n= \n" + Name1 + " \n " + Name2 + "\n has best homogenized feature offset \n" + Match + " \n==\n";
        }
    }
}