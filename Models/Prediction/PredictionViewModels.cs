namespace POSERP.Models.Prediction
{
    public class ProductPredictionViewModel
    {
        public int ProductID { get; set; }
        public string ProductName { get; set; } = "";
        public string Barcode { get; set; } = "";

        public int CurrentStock { get; set; }
        public int ReorderLevel { get; set; }

        public int PredictedDemand7Days { get; set; }
        public int PredictedDemand30Days { get; set; }

        public decimal ConfidenceScore { get; set; }

        public DateTime PredictionDate { get; set; }

        public string Recommendation { get; set; } = "";
        public string TrendDirection { get; set; } = "";

        public double AverageDailySales { get; set; }

        public int DaysUntilStockout { get; set; }

        public bool IsCriticalStock { get; set; }

        public bool IsFastSelling { get; set; }
    }

    public class TrendAnalysisViewModel
    {
        public int ProductID { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal WeeklyAverage { get; set; }
        public decimal MonthlyAverage { get; set; }
        public string TrendDirection { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
        public DateTime AnalysisDate { get; set; }
    }
}