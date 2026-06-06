using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using POSERP.Models.DataBase;
using POSERP.Models.Reports;
using System;
using System.Collections.Generic;
using System.Linq;

namespace POSERP.Controllers.Manager
{
    [Authorize(Roles = "Manager,Admin")]
    public class ReportsController : Controller
    {
        private readonly Db _db;
        public ReportsController(Db db) => _db = db;

        // ========== SALES REPORT ==========
        public IActionResult SalesReport(DateTime? startDate, DateTime? endDate)
        {
            DateTime from = startDate ?? DateTime.Today.AddDays(-30);
            DateTime to = endDate ?? DateTime.Today;
            ViewBag.StartDate = from.ToString("yyyy-MM-dd");
            ViewBag.EndDate = to.ToString("yyyy-MM-dd");

            var sales = new List<SalesReportViewModel>();
            var productDetails = new List<ProductSalesDetailViewModel>();

            using var conn = _db.GetConnection();
            conn.Open();

            // Daily summary
            string dailyQuery = @"
                SELECT CAST(SaleDate AS DATE) AS Date,
                       COUNT(*) AS SaleCount,
                       SUM(TotalAmount) AS TotalAmount,
                       SUM(DiscountAmount) AS DiscountAmount,
                       SUM(TaxAmount) AS TaxAmount,
                       SUM(TotalAmount - DiscountAmount) AS NetAmount
                FROM Sales
                WHERE SaleDate BETWEEN @from AND @to AND IsCancelled = 0
                GROUP BY CAST(SaleDate AS DATE)
                ORDER BY Date";
            using var cmd = new SqlCommand(dailyQuery, conn);
            cmd.Parameters.AddWithValue("@from", from);
            cmd.Parameters.AddWithValue("@to", to);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                sales.Add(new SalesReportViewModel
                {
                    Date = reader.GetDateTime(0),
                    SaleCount = reader.GetInt32(1),
                    TotalAmount = reader.GetDecimal(2),
                    DiscountAmount = reader.GetDecimal(3),
                    TaxAmount = reader.GetDecimal(4),
                    NetAmount = reader.GetDecimal(5)
                });
            }
            reader.Close();

            // Product breakdown
            string productQuery = @"
                SELECT p.ProductName,
                       SUM(si.Quantity) AS Quantity,
                       SUM(si.SubTotal) AS TotalSales,
                       SUM(si.Quantity * p.CostPrice) AS TotalCost
                FROM SaleItems si
                JOIN Products p ON si.ProductID = p.ProductID
                JOIN Sales s ON si.SaleID = s.SaleID
                WHERE s.SaleDate BETWEEN @from AND @to AND s.IsCancelled = 0
                GROUP BY p.ProductName
                ORDER BY TotalSales DESC";
            using var cmdProd = new SqlCommand(productQuery, conn);
            cmdProd.Parameters.AddWithValue("@from", from);
            cmdProd.Parameters.AddWithValue("@to", to);
            using var prodReader = cmdProd.ExecuteReader();
            while (prodReader.Read())
            {
                decimal totalSales = prodReader.GetDecimal(2);
                decimal totalCost = prodReader.GetDecimal(3);
                decimal grossProfit = totalSales - totalCost;
                decimal margin = totalSales > 0 ? (grossProfit / totalSales) * 100 : 0;
                productDetails.Add(new ProductSalesDetailViewModel
                {
                    ProductName = prodReader.GetString(0),
                    Quantity = prodReader.GetInt32(1),
                    TotalSales = totalSales,
                    TotalCost = totalCost,
                    GrossProfit = grossProfit,
                    ProfitMargin = margin
                });
            }

            ViewBag.SalesData = sales;
            ViewBag.ProductDetails = productDetails;
            return View();
        }

        // ========== PROFIT REPORT ==========
        public IActionResult ProfitReport(DateTime? startDate, DateTime? endDate)
        {
            DateTime from = startDate ?? DateTime.Today.AddDays(-30);
            DateTime to = endDate ?? DateTime.Today;
            ViewBag.StartDate = from.ToString("yyyy-MM-dd");
            ViewBag.EndDate = to.ToString("yyyy-MM-dd");

            var summary = new ProfitSummaryViewModel();
            var monthlyProfits = new List<dynamic>();

            using var conn = _db.GetConnection();
            conn.Open();

            // Overall summary
            string summaryQuery = @"
                SELECT 
                    SUM(s.TotalAmount) AS TotalSales,
                    SUM(s.DiscountAmount) AS TotalDiscount,
                    SUM(s.TaxAmount) AS TotalTax,
                    SUM(si.Quantity * p.CostPrice) AS TotalCost
                FROM Sales s
                JOIN SaleItems si ON s.SaleID = si.SaleID
                JOIN Products p ON si.ProductID = p.ProductID
                WHERE s.SaleDate BETWEEN @from AND @to AND s.IsCancelled = 0";
            using var cmdSum = new SqlCommand(summaryQuery, conn);
            cmdSum.Parameters.AddWithValue("@from", from);
            cmdSum.Parameters.AddWithValue("@to", to);
            using var reader = cmdSum.ExecuteReader();
            if (reader.Read())
            {
                decimal totalSales = reader.IsDBNull(0) ? 0 : reader.GetDecimal(0);
                decimal totalDiscount = reader.IsDBNull(1) ? 0 : reader.GetDecimal(1);
                decimal totalTax = reader.IsDBNull(2) ? 0 : reader.GetDecimal(2);
                decimal totalCost = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3);

                decimal grossProfit = totalSales - totalCost;
                decimal margin = totalSales > 0 ? (grossProfit / totalSales) * 100 : 0;

                summary.TotalSales = totalSales;
                summary.TotalCost = totalCost;
                summary.GrossProfit = grossProfit;
                summary.ProfitMargin = margin;
                summary.TotalDiscount = totalDiscount;
                summary.TotalTax = totalTax;
                summary.NetProfit = totalSales - totalCost - totalDiscount - totalTax;
            }
            reader.Close();

            // Monthly profit trend
            string monthlyQuery = @"
                SELECT YEAR(s.SaleDate) AS Year, MONTH(s.SaleDate) AS Month,
                       SUM(s.TotalAmount) AS Sales,
                       SUM(si.Quantity * p.CostPrice) AS Cost
                FROM Sales s
                JOIN SaleItems si ON s.SaleID = si.SaleID
                JOIN Products p ON si.ProductID = p.ProductID
                WHERE s.SaleDate BETWEEN @from AND @to AND s.IsCancelled = 0
                GROUP BY YEAR(s.SaleDate), MONTH(s.SaleDate)
                ORDER BY Year, Month";
            using var cmdMon = new SqlCommand(monthlyQuery, conn);
            cmdMon.Parameters.AddWithValue("@from", from);
            cmdMon.Parameters.AddWithValue("@to", to);
            using var monReader = cmdMon.ExecuteReader();
            while (monReader.Read())
            {
                decimal sales = monReader.IsDBNull(2) ? 0 : monReader.GetDecimal(2);
                decimal cost = monReader.IsDBNull(3) ? 0 : monReader.GetDecimal(3);

                monthlyProfits.Add(new
                {
                    YearMonth = $"{monReader.GetInt32(0)}-{monReader.GetInt32(1):00}",
                    Sales = sales,
                    Cost = cost,
                    Profit = sales - cost,
                    Margin = sales > 0 ? ((sales - cost) / sales) * 100 : 0
                });
            }

            ViewBag.Summary = summary;
            ViewBag.MonthlyProfits = monthlyProfits;
            return View();
        }

        // ========== INVENTORY REPORT ==========
        public IActionResult InventoryReport(string search = "")
        {
            var inventory = new List<InventoryReportViewModel>();
            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                SELECT p.ProductName, p.Barcode, i.QuantityInStock, p.ReorderLevel, p.CostPrice, p.SellingPrice,
                       ISNULL((SELECT SUM(si.Quantity) FROM SaleItems si JOIN Sales s ON si.SaleID = s.SaleID 
                               WHERE si.ProductID = p.ProductID AND s.SaleDate >= DATEADD(day, -30, GETDATE()) AND s.IsCancelled = 0), 0) AS TotalSold
                FROM Products p
                JOIN Inventory i ON p.ProductID = i.ProductID
                WHERE p.IsActive = 1
                  AND (@search = '' OR p.ProductName LIKE @search OR p.Barcode LIKE @search)
                ORDER BY p.ProductName";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@search", $"%{search}%");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                int stock = reader.GetInt32(2);
                int reorder = reader.GetInt32(3);
                string status = stock <= 0 ? "Out of Stock" : (stock <= reorder ? "Low Stock" : "In Stock");
                inventory.Add(new InventoryReportViewModel
                {
                    ProductName = reader.GetString(0),
                    Barcode = reader.GetString(1),
                    CurrentStock = stock,
                    ReorderLevel = reorder,
                    CostPrice = reader.GetDecimal(4),
                    SellingPrice = reader.GetDecimal(5),
                    TotalSold = reader.GetInt32(6),
                    Status = status
                });
            }
            ViewBag.Search = search;
            return View(inventory);
        }

        [Authorize(Roles = "Admin")]
        public IActionResult ExpenseReport(DateTime? startDate, DateTime? endDate)
        {
            DateTime from = startDate ?? DateTime.Today.AddDays(-30);
            DateTime to = endDate ?? DateTime.Today;

            ViewBag.StartDate = from.ToString("yyyy-MM-dd");
            ViewBag.EndDate = to.ToString("yyyy-MM-dd");

            var summary = new ExpenseSummaryViewModel();
            var expenseBreakdown = new List<ExpenseReportViewModel>();

            using var conn = _db.GetConnection();
            conn.Open();

            // ================= SALES =================

            string salesQuery = @"
        SELECT ISNULL(SUM(TotalAmount),0)
        FROM Sales
        WHERE SaleDate BETWEEN @from AND @to
          AND IsCancelled = 0";

            using (var cmd = new SqlCommand(salesQuery, conn))
            {
                cmd.Parameters.AddWithValue("@from", from);
                cmd.Parameters.AddWithValue("@to", to);

                var result = cmd.ExecuteScalar();

                summary.TotalSales =
                    result == null || result == DBNull.Value
                    ? 0
                    : Convert.ToDecimal(result);
            }

            // ================= PAYROLL =================

            string payrollQuery = @"
        SELECT ISNULL(SUM(NetSalary),0)
        FROM Payroll
        WHERE PaymentDate BETWEEN @from AND @to";

            using (var cmd = new SqlCommand(payrollQuery, conn))
            {
                cmd.Parameters.AddWithValue("@from", from);
                cmd.Parameters.AddWithValue("@to", to);

                var result = cmd.ExecuteScalar();

                summary.TotalPayroll =
                    result == null || result == DBNull.Value
                    ? 0
                    : Convert.ToDecimal(result);
            }

            // ================= EXPENSE BREAKDOWN =================

            string expenseQuery = @"
        SELECT ExpenseType,
               ISNULL(SUM(Amount),0) AS TotalAmount
        FROM Expenses
        WHERE ExpenseDate BETWEEN @from AND @to
        GROUP BY ExpenseType
        ORDER BY TotalAmount DESC";

            using (var cmd = new SqlCommand(expenseQuery, conn))
            {
                cmd.Parameters.AddWithValue("@from", from);
                cmd.Parameters.AddWithValue("@to", to);

                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    expenseBreakdown.Add(new ExpenseReportViewModel
                    {
                        ExpenseType = reader["ExpenseType"].ToString(),
                        TotalAmount = Convert.ToDecimal(reader["TotalAmount"])
                    });
                }
            }

            // ================= TOTAL EXPENSES =================

            decimal normalExpenses = expenseBreakdown.Sum(x => x.TotalAmount);

            summary.TotalExpenses = normalExpenses + summary.TotalPayroll;

            // ================= PROFIT / LOSS =================

            decimal netAmount = summary.TotalSales - summary.TotalExpenses;

            if (netAmount >= 0)
            {
                summary.NetProfit = netAmount;
                summary.NetLoss = 0;
            }
            else
            {
                summary.NetProfit = 0;
                summary.NetLoss = Math.Abs(netAmount);
            }

            ViewBag.Summary = summary;
            ViewBag.ExpenseBreakdown = expenseBreakdown;

            return View();
        }
        // ========== DASHBOARD SUMMARY (optional) ==========
        public IActionResult DashboardSummary()
        {
            using var conn = _db.GetConnection();
            conn.Open();
            // Today's sales
            string todayQuery = "SELECT ISNULL(SUM(TotalAmount),0) FROM Sales WHERE CAST(SaleDate AS DATE) = CAST(GETDATE() AS DATE) AND IsCancelled=0";
            using var cmdToday = new SqlCommand(todayQuery, conn);
            ViewBag.TodaySales = cmdToday.ExecuteScalar();

            // This month sales
            string monthQuery = "SELECT ISNULL(SUM(TotalAmount),0) FROM Sales WHERE MONTH(SaleDate)=MONTH(GETDATE()) AND YEAR(SaleDate)=YEAR(GETDATE()) AND IsCancelled=0";
            using var cmdMonth = new SqlCommand(monthQuery, conn);
            ViewBag.MonthSales = cmdMonth.ExecuteScalar();

            // Low stock count
            string lowStockQuery = "SELECT COUNT(*) FROM Products p JOIN Inventory i ON p.ProductID=i.ProductID WHERE i.QuantityInStock <= p.ReorderLevel AND i.QuantityInStock >0";
            using var cmdLow = new SqlCommand(lowStockQuery, conn);
            ViewBag.LowStockCount = cmdLow.ExecuteScalar();

            // Out of stock
            string outQuery = "SELECT COUNT(*) FROM Products p JOIN Inventory i ON p.ProductID=i.ProductID WHERE i.QuantityInStock =0";
            using var cmdOut = new SqlCommand(outQuery, conn);
            ViewBag.OutStockCount = cmdOut.ExecuteScalar();

            return View();
        }
        // ========== PARTIAL REFRESH FOR SALES REPORT ==========
        [HttpGet]
        public IActionResult RefreshSalesReport(DateTime? startDate, DateTime? endDate)
        {
            DateTime from = startDate ?? DateTime.Today.AddDays(-30);
            DateTime to = endDate ?? DateTime.Today;

            var sales = new List<SalesReportViewModel>();
            var productDetails = new List<ProductSalesDetailViewModel>();

            using var conn = _db.GetConnection();
            conn.Open();

            // Daily summary (same as in SalesReport)
            string dailyQuery = @"
        SELECT CAST(SaleDate AS DATE) AS Date,
               COUNT(*) AS SaleCount,
               SUM(TotalAmount) AS TotalAmount,
               SUM(DiscountAmount) AS DiscountAmount,
               SUM(TaxAmount) AS TaxAmount,
               SUM(TotalAmount - DiscountAmount) AS NetAmount
        FROM Sales
        WHERE SaleDate BETWEEN @from AND @to AND IsCancelled = 0
        GROUP BY CAST(SaleDate AS DATE)
        ORDER BY Date";
            using var cmd = new SqlCommand(dailyQuery, conn);
            cmd.Parameters.AddWithValue("@from", from);
            cmd.Parameters.AddWithValue("@to", to);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                sales.Add(new SalesReportViewModel
                {
                    Date = reader.GetDateTime(0),
                    SaleCount = reader.GetInt32(1),
                    TotalAmount = reader.GetDecimal(2),
                    DiscountAmount = reader.GetDecimal(3),
                    TaxAmount = reader.GetDecimal(4),
                    NetAmount = reader.GetDecimal(5)
                });
            }
            reader.Close();

            // Product breakdown
            string productQuery = @"
        SELECT p.ProductName,
               SUM(si.Quantity) AS Quantity,
               SUM(si.SubTotal) AS TotalSales,
               SUM(si.Quantity * p.CostPrice) AS TotalCost
        FROM SaleItems si
        JOIN Products p ON si.ProductID = p.ProductID
        JOIN Sales s ON si.SaleID = s.SaleID
        WHERE s.SaleDate BETWEEN @from AND @to AND s.IsCancelled = 0
        GROUP BY p.ProductName
        ORDER BY TotalSales DESC";
            using var cmdProd = new SqlCommand(productQuery, conn);
            cmdProd.Parameters.AddWithValue("@from", from);
            cmdProd.Parameters.AddWithValue("@to", to);
            using var prodReader = cmdProd.ExecuteReader();
            while (prodReader.Read())
            {
                decimal totalSales = prodReader.GetDecimal(2);
                decimal totalCost = prodReader.GetDecimal(3);
                decimal grossProfit = totalSales - totalCost;
                decimal margin = totalSales > 0 ? (grossProfit / totalSales) * 100 : 0;
                productDetails.Add(new ProductSalesDetailViewModel
                {
                    ProductName = prodReader.GetString(0),
                    Quantity = prodReader.GetInt32(1),
                    TotalSales = totalSales,
                    TotalCost = totalCost,
                    GrossProfit = grossProfit,
                    ProfitMargin = margin
                });
            }

            ViewBag.SalesData = sales;
            ViewBag.ProductDetails = productDetails;
            return PartialView("_SalesReportPartial");
        }

        // ========== PARTIAL REFRESH FOR PROFIT REPORT ==========
        [HttpGet]
        public IActionResult RefreshProfitReport(DateTime? startDate, DateTime? endDate)
        {
            DateTime from = startDate ?? DateTime.Today.AddDays(-30);
            DateTime to = endDate ?? DateTime.Today;

            var summary = new ProfitSummaryViewModel();
            var monthlyProfits = new List<dynamic>();

            using var conn = _db.GetConnection();
            conn.Open();

            // Overall summary
            string summaryQuery = @"
        SELECT 
            SUM(s.TotalAmount) AS TotalSales,
            SUM(s.DiscountAmount) AS TotalDiscount,
            SUM(s.TaxAmount) AS TotalTax,
            SUM(si.Quantity * p.CostPrice) AS TotalCost
        FROM Sales s
        JOIN SaleItems si ON s.SaleID = si.SaleID
        JOIN Products p ON si.ProductID = p.ProductID
        WHERE s.SaleDate BETWEEN @from AND @to AND s.IsCancelled = 0";
            using var cmdSum = new SqlCommand(summaryQuery, conn);
            cmdSum.Parameters.AddWithValue("@from", from);
            cmdSum.Parameters.AddWithValue("@to", to);
            using var reader = cmdSum.ExecuteReader();
            if (reader.Read())
            {
                decimal totalSales = reader.GetDecimal(0);
                decimal totalDiscount = reader.GetDecimal(1);
                decimal totalTax = reader.GetDecimal(2);
                decimal totalCost = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3);
                decimal grossProfit = totalSales - totalCost;
                decimal margin = totalSales > 0 ? (grossProfit / totalSales) * 100 : 0;
                summary.TotalSales = totalSales;
                summary.TotalCost = totalCost;
                summary.GrossProfit = grossProfit;
                summary.ProfitMargin = margin;
                summary.TotalDiscount = totalDiscount;
                summary.TotalTax = totalTax;
                summary.NetProfit = totalSales - totalCost - totalDiscount - totalTax;
            }
            reader.Close();

            // Monthly profit trend
            string monthlyQuery = @"
        SELECT YEAR(s.SaleDate) AS Year, MONTH(s.SaleDate) AS Month,
               SUM(s.TotalAmount) AS Sales,
               SUM(si.Quantity * p.CostPrice) AS Cost
        FROM Sales s
        JOIN SaleItems si ON s.SaleID = si.SaleID
        JOIN Products p ON si.ProductID = p.ProductID
        WHERE s.SaleDate BETWEEN @from AND @to AND s.IsCancelled = 0
        GROUP BY YEAR(s.SaleDate), MONTH(s.SaleDate)
        ORDER BY Year, Month";
            using var cmdMon = new SqlCommand(monthlyQuery, conn);
            cmdMon.Parameters.AddWithValue("@from", from);
            cmdMon.Parameters.AddWithValue("@to", to);
            using var monReader = cmdMon.ExecuteReader();
            while (monReader.Read())
            {
                decimal sales = monReader.GetDecimal(2);
                decimal cost = monReader.GetDecimal(3);
                monthlyProfits.Add(new
                {
                    YearMonth = $"{monReader.GetInt32(0)}-{monReader.GetInt32(1):00}",
                    Sales = sales,
                    Cost = cost,
                    Profit = sales - cost,
                    Margin = sales > 0 ? ((sales - cost) / sales) * 100 : 0
                });
            }

            ViewBag.Summary = summary;
            ViewBag.MonthlyProfits = monthlyProfits;
            return PartialView("_ProfitReportPartial");
        }

        // ========== PARTIAL REFRESH FOR INVENTORY REPORT ==========
        [HttpGet]
        public IActionResult RefreshInventoryReport(string search = "")
        {
            var inventory = new List<InventoryReportViewModel>();
            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
        SELECT p.ProductName, p.Barcode, i.QuantityInStock, p.ReorderLevel, p.CostPrice, p.SellingPrice,
               ISNULL((SELECT SUM(si.Quantity) FROM SaleItems si JOIN Sales s ON si.SaleID = s.SaleID 
                       WHERE si.ProductID = p.ProductID AND s.SaleDate >= DATEADD(day, -30, GETDATE()) AND s.IsCancelled = 0), 0) AS TotalSold
        FROM Products p
        JOIN Inventory i ON p.ProductID = i.ProductID
        WHERE p.IsActive = 1
          AND (@search = '' OR p.ProductName LIKE @search OR p.Barcode LIKE @search)
        ORDER BY p.ProductName";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@search", $"%{search}%");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                int stock = reader.GetInt32(2);
                int reorder = reader.GetInt32(3);
                string status = stock <= 0 ? "Out of Stock" : (stock <= reorder ? "Low Stock" : "In Stock");
                inventory.Add(new InventoryReportViewModel
                {
                    ProductName = reader.GetString(0),
                    Barcode = reader.GetString(1),
                    CurrentStock = stock,
                    ReorderLevel = reorder,
                    CostPrice = reader.GetDecimal(4),
                    SellingPrice = reader.GetDecimal(5),
                    TotalSold = reader.GetInt32(6),
                    Status = status
                });
            }
            return PartialView("_InventoryReportPartial", inventory);
        }

    }
}