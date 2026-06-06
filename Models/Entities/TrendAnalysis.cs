using System;

namespace POSERP.Models.Entities
{
    public class TrendAnalysis
    {
        public int TrendID { get; set; }
        public int ProductID { get; set; }
        public DateTime AnalysisDate { get; set; }
        public decimal? WeeklyAverage { get; set; }
        public decimal? MonthlyAverage { get; set; }
        public string TrendDirection { get; set; }
        public string Recommendation { get; set; }

        public virtual Product Product { get; set; }
    }
}