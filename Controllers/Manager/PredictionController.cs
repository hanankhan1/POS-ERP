using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using POSERP.Models.DataBase;
using POSERP.Models.Prediction;
using POSERP.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace POSERP.Controllers.Manager
{
    [Authorize(Roles = "Manager,Admin")]
    public class PredictionController : Controller
    {
        private readonly Db _db;
        private readonly EmailService _emailService;
        public PredictionController(Db db, EmailService emailService)
        {
            _db = db;
            _emailService = emailService;
        }
        // ========== MAIN PREDICTION DASHBOARD ==========
        public IActionResult Index(string search = "")
        {
            var predictions = new List<ProductPredictionViewModel>();
            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                SELECT p.ProductID, p.ProductName, p.Barcode, 
                       ISNULL(i.QuantityInStock, 0) AS CurrentStock,
                       p.ReorderLevel,
                       ISNULL(pr.PredictedDemand, 0) AS PredictedDemand,
                       ISNULL(pr.ConfidenceScore, 0) AS ConfidenceScore,
                       pr.PredictionDate,
                       ISNULL(ta.TrendDirection, 'Stable') AS TrendDirection,
                       ISNULL(ta.Recommendation, '') AS Recommendation
                FROM Products p
                LEFT JOIN Inventory i ON p.ProductID = i.ProductID
                LEFT JOIN (
                    SELECT ProductID, PredictedDemand, ConfidenceScore, PredictionDate,
                           ROW_NUMBER() OVER (PARTITION BY ProductID ORDER BY PredictionDate DESC) AS rn
                    FROM Predictions
                ) pr ON p.ProductID = pr.ProductID AND pr.rn = 1
                LEFT JOIN (
                    SELECT ProductID, TrendDirection, Recommendation, AnalysisDate,
                           ROW_NUMBER() OVER (PARTITION BY ProductID ORDER BY AnalysisDate DESC) AS rn
                    FROM TrendAnalysis
                ) ta ON p.ProductID = ta.ProductID AND ta.rn = 1
                WHERE p.IsActive = 1
                  AND (@search = '' OR p.ProductName LIKE @search OR p.Barcode LIKE @search)
                ORDER BY p.ProductName";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@search", $"%{search}%");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                // Calculate predicted demand for 7 and 30 days based on average daily sales (simplified)
                // For demo, we use the stored PredictedDemand as 7‑day; 30‑day is 4x that (approx)
                int pred7 =
    reader.IsDBNull(5)
        ? 0
        : reader.GetInt32(5);

                int pred30 = pred7 * 4;

                double avgDailySales =
                    pred7 > 0
                        ? pred7 / 7.0
                        : 0;

                int currentStock =
                    reader.GetInt32(3);

                int daysLeft =
                    avgDailySales > 0
                        ? (int)Math.Floor(
                            currentStock /
                            avgDailySales)
                        : 999;

                bool critical =
                    daysLeft <= 2;

                predictions.Add(new ProductPredictionViewModel
                {
                    ProductID = reader.GetInt32(0),
                    ProductName = reader.GetString(1),
                    Barcode = reader.GetString(2),

                    CurrentStock = currentStock,

                    ReorderLevel = reader.GetInt32(4),

                    PredictedDemand7Days = pred7,

                    PredictedDemand30Days = pred30,

                    ConfidenceScore =
                        reader.IsDBNull(6)
                            ? 0
                            : reader.GetDecimal(6),

                    PredictionDate =
                        reader.IsDBNull(7)
                            ? DateTime.MinValue
                            : reader.GetDateTime(7),

                    TrendDirection =
                        reader.IsDBNull(8)
                            ? "Stable"
                            : reader.GetString(8),

                    Recommendation =
                        reader.IsDBNull(9)
                            ? ""
                            : reader.GetString(9),

                    AverageDailySales = avgDailySales,

                    DaysUntilStockout = daysLeft,

                    IsCriticalStock = critical,

                    IsFastSelling = avgDailySales >= 20
                });
            }
            ViewBag.Search = search;
            return View(predictions);
        }

        // ========== GENERATE PREDICTIONS (Manual or Scheduled) ==========
        [HttpPost]
        public async Task<IActionResult> GeneratePredictions()
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string productQuery = "SELECT ProductID FROM Products WHERE IsActive = 1";
            var products = new List<int>();
            using var cmdProd = new SqlCommand(productQuery, conn);
            using var readerProd = cmdProd.ExecuteReader();
            while (readerProd.Read())
                products.Add(readerProd.GetInt32(0));
            readerProd.Close();

            var lowStockAlerts = new List<string>(); 

            foreach (int pid in products)
            {
                // Calculate daily average sales for last 30 days
                string avgQuery = @"
            SELECT ISNULL(AVG(DailyQty), 0) AS AvgDaily
            FROM (
                SELECT CAST(s.SaleDate AS DATE) AS SaleDate, SUM(si.Quantity) AS DailyQty
                FROM Sales s
                JOIN SaleItems si ON s.SaleID = si.SaleID
                WHERE si.ProductID = @pid AND s.SaleDate >= DATEADD(day, -30, GETDATE()) AND s.IsCancelled = 0
                GROUP BY CAST(s.SaleDate AS DATE)
            ) AS Daily";
                using var cmdAvg = new SqlCommand(avgQuery, conn);
                cmdAvg.Parameters.AddWithValue("@pid", pid);
                decimal avgDaily = Convert.ToDecimal(cmdAvg.ExecuteScalar());
                bool fastSelling = IsFastSelling(avgDaily);
                int predictedDemand = (int)Math.Ceiling(avgDaily * 7);
                decimal confidence = avgDaily > 0 ? Math.Min(100, 50 + (avgDaily * 2)) : 50;

                // Get current stock
                string stockQuery = "SELECT QuantityInStock FROM Inventory WHERE ProductID = @pid";
                using var cmdStock = new SqlCommand(stockQuery, conn);
                cmdStock.Parameters.AddWithValue("@pid", pid);
                object stockObj = cmdStock.ExecuteScalar();

                int currentStock =
                    stockObj == null ||
                    stockObj == DBNull.Value
                        ? 0
                        : Convert.ToInt32(stockObj);

                // Days until stockout
                int daysLeft = DaysUntilStockout(currentStock, avgDaily);
                string stockoutWarning = "";
                if (daysLeft <= 2 && avgDaily > 0)
                {
                    stockoutWarning = $"⚠️ Stock may finish in {daysLeft} days!";
                    lowStockAlerts.Add($"Product ID {pid}: {stockoutWarning}");
                }

                // Insert prediction
                string insertPred = @"
            INSERT INTO Predictions (ProductID, PredictionDate, PredictedDemand, ConfidenceScore, GeneratedAt)
            VALUES (@pid, GETDATE(), @demand, @conf, GETDATE())";
                using var cmdIns = new SqlCommand(insertPred, conn);
                cmdIns.Parameters.AddWithValue("@pid", pid);
                cmdIns.Parameters.AddWithValue("@demand", predictedDemand);
                cmdIns.Parameters.AddWithValue("@conf", confidence);
                cmdIns.ExecuteNonQuery();

                // Trend analysis (weekly vs monthly)
                string weeklyAvg = @"
            SELECT ISNULL(AVG(si.Quantity), 0) FROM SaleItems si
            JOIN Sales s ON si.SaleID = s.SaleID
            WHERE si.ProductID = @pid AND s.SaleDate >= DATEADD(day, -7, GETDATE()) AND s.IsCancelled = 0";
                string monthlyAvg = @"
            SELECT ISNULL(AVG(si.Quantity), 0) FROM SaleItems si
            JOIN Sales s ON si.SaleID = s.SaleID
            WHERE si.ProductID = @pid AND s.SaleDate >= DATEADD(day, -30, GETDATE()) AND s.IsCancelled = 0";
                using var cmdWeek = new SqlCommand(weeklyAvg, conn);
                cmdWeek.Parameters.AddWithValue("@pid", pid);
                decimal weekAvg = Convert.ToDecimal(cmdWeek.ExecuteScalar());
                using var cmdMonth = new SqlCommand(monthlyAvg, conn);
                cmdMonth.Parameters.AddWithValue("@pid", pid);
                decimal monthAvg = Convert.ToDecimal(cmdMonth.ExecuteScalar());

                string trend = "Stable";
                string recommendation = "Maintain current stock levels.";

                if (fastSelling)
                {
                    recommendation +=
                        " Fast-selling product. Keep additional stock available.";
                }
                if (weekAvg > monthAvg * 1.2m)
                {
                    trend = "Up";
                    recommendation = "Demand increasing. Consider raising reorder level or ordering extra stock.";
                }
                else if (weekAvg < monthAvg * 0.8m)
                {
                    trend = "Down";
                    recommendation = "Demand decreasing. Reduce reorder quantities to avoid overstock.";
                }

                // Add weekend pattern detection
                bool weekendSpike = IsWeekendDemandHigher(pid, conn);
                if (weekendSpike)
                {
                    recommendation += " Demand may increase before weekends. Plan extra stock for Friday/Saturday.";
                }

                // Insert trend analysis
                string insertTrend = @"
            INSERT INTO TrendAnalysis (ProductID, AnalysisDate, WeeklyAverage, MonthlyAverage, TrendDirection, Recommendation)
            VALUES (@pid, GETDATE(), @week, @month, @trend, @rec)";
                using var cmdTrend = new SqlCommand(insertTrend, conn);
                cmdTrend.Parameters.AddWithValue("@pid", pid);
                cmdTrend.Parameters.AddWithValue("@week", weekAvg);
                cmdTrend.Parameters.AddWithValue("@month", monthAvg);
                cmdTrend.Parameters.AddWithValue("@trend", trend);
                cmdTrend.Parameters.AddWithValue("@rec", recommendation + (daysLeft <= 2 ? " " + stockoutWarning : ""));
                cmdTrend.ExecuteNonQuery();
            }

            // Send email alerts for critical low stock
            if (lowStockAlerts.Any())
            {
                string alertEmails = GetAlertEmails(conn); // from settings table
                if (!string.IsNullOrEmpty(alertEmails))
                {
                    string subject = "Inventory Stockout Warning";
                    string body = "<h3>Products at risk of stockout within 2 days:</h3><ul>";
                    foreach (var alert in lowStockAlerts)
                        body += $"<li>{alert}</li>";
                    body += "</ul><p>Please review and reorder.</p>";
                    await _emailService.SendEmailAsync(alertEmails, subject, body);
                }
            }

            TempData["Success"] = "Predictions, trend analysis, and stockout warnings generated.";
            return RedirectToAction("Index");
        }

        private string GetAlertEmails(SqlConnection conn)
        {
            string query =
                "SELECT TOP 1 NotifyEmails FROM AlertSettings";

            using var cmd = new SqlCommand(query, conn);

            var result = cmd.ExecuteScalar();

            return result?.ToString() ?? "";
        }

        // ========== DETAILED PRODUCT PREDICTION ==========
        public IActionResult ProductDetails(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                SELECT p.ProductID, p.ProductName, p.Barcode, p.ReorderLevel,
                       ISNULL(i.QuantityInStock, 0) AS CurrentStock,
                       pr.PredictedDemand, pr.ConfidenceScore, pr.PredictionDate,
                       ta.WeeklyAverage, ta.MonthlyAverage, ta.TrendDirection, ta.Recommendation,
                       ta.AnalysisDate
                FROM Products p
                LEFT JOIN Inventory i ON p.ProductID = i.ProductID
                LEFT JOIN (
                    SELECT ProductID, PredictedDemand, ConfidenceScore, PredictionDate,
                           ROW_NUMBER() OVER (PARTITION BY ProductID ORDER BY PredictionDate DESC) AS rn
                    FROM Predictions
                ) pr ON p.ProductID = pr.ProductID AND pr.rn = 1
                LEFT JOIN (
                    SELECT ProductID, WeeklyAverage, MonthlyAverage, TrendDirection, Recommendation, AnalysisDate,
                           ROW_NUMBER() OVER (PARTITION BY ProductID ORDER BY AnalysisDate DESC) AS rn
                    FROM TrendAnalysis
                ) ta ON p.ProductID = ta.ProductID AND ta.rn = 1
                WHERE p.ProductID = @id";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return NotFound();
            var model = new
            {
                ProductID = reader.GetInt32(0),
                ProductName = reader.GetString(1),
                Barcode = reader.GetString(2),
                ReorderLevel = reader.GetInt32(3),
                CurrentStock = reader.GetInt32(4),
                PredictedDemand = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                ConfidenceScore = reader.IsDBNull(6) ? 0 : reader.GetDecimal(6),
                PredictionDate = reader.IsDBNull(7) ? DateTime.MinValue : reader.GetDateTime(7),
                WeeklyAverage = reader.IsDBNull(8) ? 0 : reader.GetDecimal(8),
                MonthlyAverage = reader.IsDBNull(9) ? 0 : reader.GetDecimal(9),
                TrendDirection = reader.IsDBNull(10) ? "Stable" : reader.GetString(10),
                Recommendation = reader.IsDBNull(11) ? "" : reader.GetString(11),
                AnalysisDate = reader.IsDBNull(12) ? DateTime.MinValue : reader.GetDateTime(12)
            };
            return View(model);
        }

        // ========== LOW STOCK PREDICTIONS (Actionable) ==========
        public IActionResult LowStockPredictions()
        {
            var predictions = new List<ProductPredictionViewModel>();
            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                SELECT p.ProductID, p.ProductName, p.Barcode, 
                       ISNULL(i.QuantityInStock, 0) AS CurrentStock,
                       p.ReorderLevel,
                       ISNULL(pr.PredictedDemand, 0) AS PredictedDemand,
                       pr.ConfidenceScore,
                       ta.Recommendation
                FROM Products p
                LEFT JOIN Inventory i ON p.ProductID = i.ProductID
                LEFT JOIN (
                    SELECT ProductID, PredictedDemand, ConfidenceScore,
                           ROW_NUMBER() OVER (PARTITION BY ProductID ORDER BY PredictionDate DESC) AS rn
                    FROM Predictions
                ) pr ON p.ProductID = pr.ProductID AND pr.rn = 1
                LEFT JOIN (
                    SELECT ProductID, Recommendation,
                           ROW_NUMBER() OVER (PARTITION BY ProductID ORDER BY AnalysisDate DESC) AS rn
                    FROM TrendAnalysis
                ) ta ON p.ProductID = ta.ProductID AND ta.rn = 1
                WHERE p.IsActive = 1 
                  AND i.QuantityInStock <= p.ReorderLevel
                ORDER BY (i.QuantityInStock * 1.0 / p.ReorderLevel) ASC";
            using var cmd = new SqlCommand(query, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                predictions.Add(new ProductPredictionViewModel
                {
                    ProductID = reader.GetInt32(0),
                    ProductName = reader.GetString(1),
                    Barcode = reader.GetString(2),
                    CurrentStock = reader.GetInt32(3),
                    ReorderLevel = reader.GetInt32(4),
                    PredictedDemand7Days = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                    ConfidenceScore = reader.IsDBNull(6) ? 0 : reader.GetDecimal(6),
                    Recommendation = reader.IsDBNull(7) ? "" : reader.GetString(7)
                });
            }
            return View(predictions);
        }
        // Calculate days until stockout based on average daily sales
        private int DaysUntilStockout(int currentStock, decimal avgDailySales)
        {
            if (avgDailySales <= 0)
                return 999;

            return (int)Math.Floor(currentStock / avgDailySales);
        }

        // Detect weekend demand increase (compare weekend vs weekday averages)
        private bool IsWeekendDemandHigher(int productId, SqlConnection conn)
        {
            string query = @"
        SELECT 
            AVG(CASE WHEN DATEPART(dw, SaleDate) IN (1, 7) THEN si.Quantity ELSE NULL END) AS WeekendAvg,
            AVG(CASE WHEN DATEPART(dw, SaleDate) NOT IN (1, 7) THEN si.Quantity ELSE NULL END) AS WeekdayAvg
        FROM SaleItems si
        JOIN Sales s ON si.SaleID = s.SaleID
        WHERE si.ProductID = @pid AND s.SaleDate >= DATEADD(day, -30, GETDATE()) AND s.IsCancelled = 0";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@pid", productId);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                decimal weekendAvg = reader.IsDBNull(0) ? 0 : Convert.ToDecimal(reader.GetValue(0));
                decimal weekdayAvg = reader.IsDBNull(1) ? 0 : Convert.ToDecimal(reader.GetValue(1));
                return weekendAvg > weekdayAvg * 1.2m; // 20% higher on weekends
            }
            return false;
        }
        private bool IsFastSelling(decimal avgDailySales)
        {
            return avgDailySales >= 20;
        }

        




    }
}