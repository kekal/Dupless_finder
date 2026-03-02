using System;
using System.Collections.Generic;
using System.Linq;
using Dupples_finder_UI.DTO;
using OpenCvSharp;
using Xunit;

namespace Dupless_finder_Tests
{
    public class PairSimilarityInfoTests
    {
        private static Mat CreateTestMat()
        {
            return new Mat(1, 128, MatType.CV_32FC1);
        }

        [Fact]
        public void Equals_TrueForSameOrder()
        {
            var mat1 = CreateTestMat();
            var mat2 = CreateTestMat();

            var hash1 = new KeyValuePair<string, Mat>("fileA.jpg", mat1);
            var hash2 = new KeyValuePair<string, Mat>("fileB.jpg", mat2);

            var pair1 = new PairSimilarityInfo(hash1, hash2, 0.95);
            var pair2 = new PairSimilarityInfo(hash1, hash2, 0.90);

            Assert.True(pair1.Equals(pair2));

            mat1.Dispose();
            mat2.Dispose();
        }

        [Fact]
        public void Equals_TrueForReversedOrder()
        {
            var mat1 = CreateTestMat();
            var mat2 = CreateTestMat();

            var hash1 = new KeyValuePair<string, Mat>("fileA.jpg", mat1);
            var hash2 = new KeyValuePair<string, Mat>("fileB.jpg", mat2);

            var pair1 = new PairSimilarityInfo(hash1, hash2, 0.95);
            var pair2 = new PairSimilarityInfo(hash2, hash1, 0.90);

            Assert.True(pair1.Equals(pair2));

            mat1.Dispose();
            mat2.Dispose();
        }

        [Fact]
        public void Equals_FalseForDifferentPairs()
        {
            var mat1 = CreateTestMat();
            var mat2 = CreateTestMat();
            var mat3 = CreateTestMat();
            var mat4 = CreateTestMat();

            var hash1 = new KeyValuePair<string, Mat>("fileA.jpg", mat1);
            var hash2 = new KeyValuePair<string, Mat>("fileB.jpg", mat2);
            var hash3 = new KeyValuePair<string, Mat>("fileC.jpg", mat3);
            var hash4 = new KeyValuePair<string, Mat>("fileD.jpg", mat4);

            var pair1 = new PairSimilarityInfo(hash1, hash2, 0.95);
            var pair2 = new PairSimilarityInfo(hash3, hash4, 0.90);

            Assert.False(pair1.Equals(pair2));

            mat1.Dispose();
            mat2.Dispose();
            mat3.Dispose();
            mat4.Dispose();
        }

        [Fact]
        public void GetHashCode_SameForReversedOrder()
        {
            var mat1 = CreateTestMat();
            var mat2 = CreateTestMat();

            var hash1 = new KeyValuePair<string, Mat>("fileA.jpg", mat1);
            var hash2 = new KeyValuePair<string, Mat>("fileB.jpg", mat2);

            var pair1 = new PairSimilarityInfo(hash1, hash2, 0.95);
            var pair2 = new PairSimilarityInfo(hash2, hash1, 0.90);

            Assert.Equal(pair1.GetHashCode(), pair2.GetHashCode());

            mat1.Dispose();
            mat2.Dispose();
        }

        [Fact]
        public void Distinct_RemovesDuplicatePairs()
        {
            var mat1 = CreateTestMat();
            var mat2 = CreateTestMat();

            var hash1 = new KeyValuePair<string, Mat>("fileA.jpg", mat1);
            var hash2 = new KeyValuePair<string, Mat>("fileB.jpg", mat2);

            var pair1 = new PairSimilarityInfo(hash1, hash2, 0.95);
            var pair2 = new PairSimilarityInfo(hash2, hash1, 0.90); // Reversed order

            var list = new List<PairSimilarityInfo> { pair1, pair2 };
            var distinct = list.Distinct().ToList();

            Assert.Single(distinct);

            mat1.Dispose();
            mat2.Dispose();
        }
    }
}
