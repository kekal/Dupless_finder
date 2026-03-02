using System;

namespace Dupples_finder_UI.Data.Entities
{
    public class SimilarityResult
    {
        public int Id { get; set; }
        public int Photo1Id { get; set; }
        public Photo Photo1 { get; set; }
        public int Photo2Id { get; set; }
        public Photo Photo2 { get; set; }
        public double Score { get; set; }
        public DateTime CompareDate { get; set; }
    }
}
