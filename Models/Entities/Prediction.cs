using System;

namespace POSERP.Models.Entities
{
    public class Prediction
    {
        public int PredictionID { get; set; }
        public int ProductID { get; set; }
        public DateTime PredictionDate { get; set; }
        public int? PredictedDemand { get; set; }
        public decimal? ConfidenceScore { get; set; }
        public DateTime GeneratedAt { get; set; } = DateTime.Now;

        public virtual Product Product { get; set; }
    }
}