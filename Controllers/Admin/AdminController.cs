using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using POSERP.Models.DataBase;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace POSERP.Controllers.Admin
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly Db _db;

        public AdminController(Db db)
        {
            _db = db;
        }

        // ========== INDEX - Redirect to Dashboard ==========
        public IActionResult Index()
        {
            return RedirectToAction("Dashboard");
        }

        // ========== MAIN ADMIN DASHBOARD ==========
        public IActionResult Dashboard()
        {
            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();

                // Today's Revenue
                string todayRevenueQuery = @"
                    SELECT ISNULL(SUM(TotalAmount), 0) 
                    FROM Sales 
                    WHERE PaymentStatus = 'Paid' 
                    AND CAST(SaleDate AS DATE) = CAST(GETDATE() AS DATE)
                    AND IsCancelled = 0";
                ViewBag.TodayRevenue = Convert.ToDecimal(new SqlCommand(todayRevenueQuery, con).ExecuteScalar()).ToString("N2");

                // Total Orders (last 30 days)
                string totalOrdersQuery = @"
                    SELECT COUNT(*) 
                    FROM Sales 
                    WHERE SaleDate >= DATEADD(day, -30, GETDATE()) 
                    AND IsCancelled = 0";
                ViewBag.TotalOrders = (int)new SqlCommand(totalOrdersQuery, con).ExecuteScalar();

                // Total Active Customers
                string totalCustomersQuery = "SELECT COUNT(*) FROM Customers WHERE IsActive = 1";
                ViewBag.TotalCustomers = (int)new SqlCommand(totalCustomersQuery, con).ExecuteScalar();

                // Low Stock Items
                string lowStockQuery = @"
                    SELECT COUNT(*) 
                    FROM Products p
                    JOIN Inventory i ON p.ProductID = i.ProductID
                    WHERE p.IsActive = 1 AND i.QuantityInStock > 0 AND i.QuantityInStock <= p.ReorderLevel";
                ViewBag.LowStockCount = (int)new SqlCommand(lowStockQuery, con).ExecuteScalar();

                // Sales Trend for Chart (last 7 days) - FIXED VERSION
                var salesLabels = new List<string>();
                var salesValues = new List<decimal>();
                var profitValues = new List<decimal>();

                // First get daily sales
                string dailySalesQuery = @"
                    SELECT 
                        FORMAT(SaleDate, 'ddd') AS DayName,
                        CAST(SaleDate AS DATE) AS SaleDateOnly,
                        ISNULL(SUM(TotalAmount), 0) AS DailySales
                    FROM Sales s
                    WHERE SaleDate >= DATEADD(day, -6, CAST(GETDATE() AS DATE))
                    AND IsCancelled = 0
                    GROUP BY FORMAT(SaleDate, 'ddd'), CAST(SaleDate AS DATE)
                    ORDER BY MIN(SaleDate)";

                var dailySales = new Dictionary<DateTime, decimal>();
                var dayNames = new Dictionary<DateTime, string>();

                using (var cmd = new SqlCommand(dailySalesQuery, con))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        DateTime date = reader.GetDateTime(1);
                        decimal sales = reader.GetDecimal(2);
                        string dayName = reader.GetString(0);

                        salesLabels.Add(dayName);
                        salesValues.Add(sales);
                        dailySales[date] = sales;
                        dayNames[date] = dayName;
                    }
                }

                // Get daily profit separately using a simpler query
                foreach (var date in dailySales.Keys)
                {
                    string profitQuery = @"
                        SELECT ISNULL(SUM(si.Quantity * (si.UnitPrice - p.CostPrice)), 0) AS DailyProfit
                        FROM Sales s
                        JOIN SaleItems si ON s.SaleID = si.SaleID
                        JOIN Products p ON si.ProductID = p.ProductID
                        WHERE CAST(s.SaleDate AS DATE) = @date
                        AND s.IsCancelled = 0";

                    using (var cmd = new SqlCommand(profitQuery, con))
                    {
                        cmd.Parameters.AddWithValue("@date", date);
                        decimal profit = Convert.ToDecimal(cmd.ExecuteScalar());
                        profitValues.Add(profit);
                    }
                }

                ViewBag.SalesLabels = salesLabels;
                ViewBag.SalesValues = salesValues;
                ViewBag.ProfitValues = profitValues;

                // Sales by Category - FIXED VERSION
                var categoryNames = new List<string>();
                var categoryPercentages = new List<decimal>();

                string categoryQuery = @"
                    SELECT TOP 4 
                        c.CategoryName, 
                        ISNULL(SUM(si.Quantity * si.UnitPrice), 0) AS TotalSales
                    FROM Categories c
                    INNER JOIN Products p ON c.CategoryID = p.CategoryID
                    LEFT JOIN SaleItems si ON p.ProductID = si.ProductID
                    LEFT JOIN Sales s ON si.SaleID = s.SaleID AND (s.IsCancelled = 0 OR s.IsCancelled IS NULL)
                    WHERE p.IsActive = 1
                    GROUP BY c.CategoryName
                    ORDER BY TotalSales DESC";

                using (var cmd = new SqlCommand(categoryQuery, con))
                using (var reader = cmd.ExecuteReader())
                {
                    decimal total = 0;
                    var tempCategories = new List<(string Name, decimal Sales)>();
                    while (reader.Read())
                    {
                        decimal sales = reader.GetDecimal(1);
                        tempCategories.Add((reader.GetString(0), sales));
                        total += sales;
                    }
                    foreach (var cat in tempCategories)
                    {
                        categoryNames.Add(cat.Name);
                        categoryPercentages.Add(total > 0 ? (cat.Sales / total) * 100 : 0);
                    }
                }
                ViewBag.CategoryNames = categoryNames;
                ViewBag.CategoryPercentages = categoryPercentages;

                // Fraud Alerts (Recent)
                var fraudAlerts = new List<dynamic>();
                try
                {
                    string fraudQuery = @"
                        SELECT TOP 3 fa.AlertID, fa.AlertType, fa.Description, fa.CreatedAt, fa.RiskLevel,
                               u.FullName AS CashierName
                        FROM FraudAlerts fa
                        JOIN Sales s ON fa.SaleID = s.SaleID
                        JOIN Users u ON s.CashierID = u.UserID
                        WHERE fa.Status = 'Active'
                        ORDER BY fa.CreatedAt DESC";
                    using (var cmd = new SqlCommand(fraudQuery, con))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            fraudAlerts.Add(new
                            {
                                AlertID = reader.GetInt32(0),
                                AlertType = reader.GetString(1),
                                Description = reader.GetString(2),
                                CreatedAt = reader.GetDateTime(3),
                                RiskLevel = reader.GetString(4),
                                CashierName = reader.GetString(5)
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Table might not exist yet
                    Console.WriteLine($"FraudAlerts table error: {ex.Message}");
                }
                ViewBag.FraudAlerts = fraudAlerts;

                // Smart Inventory Predictions
                var predictions = new List<dynamic>();
                try
                {
                    string predQuery = @"
                        SELECT TOP 3 p.ProductID, p.ProductName, i.QuantityInStock, p.ReorderLevel,
                               CASE 
                                   WHEN i.QuantityInStock <= 2 THEN 'Critical'
                                   WHEN i.QuantityInStock <= p.ReorderLevel THEN 'Low'
                                   ELSE 'Normal'
                               END AS StockStatus
                        FROM Products p
                        JOIN Inventory i ON p.ProductID = i.ProductID
                        WHERE p.IsActive = 1 AND i.QuantityInStock <= p.ReorderLevel
                        ORDER BY i.QuantityInStock ASC";
                    using (var cmd = new SqlCommand(predQuery, con))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            predictions.Add(new
                            {
                                ProductID = reader.GetInt32(0),
                                ProductName = reader.GetString(1),
                                QuantityInStock = reader.GetInt32(2),
                                ReorderLevel = reader.GetInt32(3),
                                StockStatus = reader.GetString(4)
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Predictions error: {ex.Message}");
                }
                ViewBag.Predictions = predictions;

                // Recent Orders
                var recentOrders = new List<dynamic>();
                string ordersQuery = @"
                    SELECT TOP 5 
                        s.SaleID, 
                        ISNULL(c.FullName, 'Walk-in Customer') AS CustomerName,
                        s.TotalAmount, 
                        s.PaymentStatus,
                        s.SaleDate
                    FROM Sales s
                    LEFT JOIN Customers c ON s.CustomerID = c.CustomerID
                    WHERE s.IsCancelled = 0
                    ORDER BY s.SaleDate DESC";
                using (var cmd = new SqlCommand(ordersQuery, con))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        recentOrders.Add(new
                        {
                            SaleID = reader.GetInt32(0),
                            CustomerName = reader.GetString(1),
                            TotalAmount = reader.GetDecimal(2),
                            PaymentStatus = reader.GetString(3),
                            SaleDate = reader.GetDateTime(4)
                        });
                    }
                }
                ViewBag.RecentOrders = recentOrders;

                // System Stats
                string activeUsersQuery = "SELECT COUNT(*) FROM Users WHERE Status = 'Active'";
                ViewBag.ActiveUsers = (int)new SqlCommand(activeUsersQuery, con).ExecuteScalar();

                string totalProductsQuery = "SELECT COUNT(*) FROM Products WHERE IsActive = 1";
                ViewBag.TotalProducts = (int)new SqlCommand(totalProductsQuery, con).ExecuteScalar();

                // Pending Tasks
                int pendingPO = 0;
                int pendingTransfer = 0;
                try
                {
                    string poQuery = "SELECT COUNT(*) FROM PurchaseOrders WHERE Status = 'Pending'";
                    pendingPO = (int)new SqlCommand(poQuery, con).ExecuteScalar();
                }
                catch { }
                try
                {
                    string transferQuery = "SELECT COUNT(*) FROM StockTransfers WHERE Status = 'Pending'";
                    pendingTransfer = (int)new SqlCommand(transferQuery, con).ExecuteScalar();
                }
                catch
                {
                    pendingTransfer = 0;
                }
                ViewBag.PendingTasks = pendingPO + pendingTransfer;

                // Today's Attendance percentage
                int attendancePercent = 0;
                try
                {
                    string attendanceQuery = @"
                        SELECT 
                            CASE WHEN COUNT(*) > 0 THEN 
                                COUNT(CASE WHEN a.Status = 'Present' THEN 1 END) * 100.0 / COUNT(*) 
                            ELSE 0 END AS PresentPercent
                        FROM Attendance a
                        WHERE a.AttendanceDate = CAST(GETDATE() AS DATE)";
                    object attResult = new SqlCommand(attendanceQuery, con).ExecuteScalar();
                    attendancePercent = attResult != DBNull.Value ? Convert.ToInt32(attResult) : 0;
                }
                catch { }
                ViewBag.AttendancePercent = attendancePercent;

                // Low Stock detailed items
                var lowStockItems = new List<dynamic>();
                string lowStockDetailQuery = @"
                    SELECT TOP 3 p.ProductName, ISNULL(s.SupplierName, 'N/A') AS SupplierName, 
                           i.QuantityInStock, p.ReorderLevel
                    FROM Products p
                    JOIN Inventory i ON p.ProductID = i.ProductID
                    LEFT JOIN Suppliers s ON p.SupplierID = s.SupplierID
                    WHERE p.IsActive = 1 AND i.QuantityInStock > 0 AND i.QuantityInStock <= p.ReorderLevel
                    ORDER BY i.QuantityInStock ASC";
                using (var cmd = new SqlCommand(lowStockDetailQuery, con))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lowStockItems.Add(new
                        {
                            ProductName = reader.GetString(0),
                            SupplierName = reader.GetString(1),
                            QuantityInStock = reader.GetInt32(2),
                            ReorderLevel = reader.GetInt32(3)
                        });
                    }
                }
                ViewBag.LowStockItems = lowStockItems;
            }

            return View();
        }

        // ========== GET FRAUD ALERTS ==========
        [HttpGet]
        public IActionResult GetFraudAlerts()
        {
            var alerts = new List<object>();
            try
            {
                using var conn = _db.GetConnection();
                conn.Open();
                string query = @"
                    SELECT TOP 3 fa.AlertID, fa.AlertType, fa.Description, fa.CreatedAt, fa.RiskLevel,
                           u.FullName AS CashierName
                    FROM FraudAlerts fa
                    JOIN Sales s ON fa.SaleID = s.SaleID
                    JOIN Users u ON s.CashierID = u.UserID
                    WHERE fa.Status = 'Active'
                    ORDER BY fa.CreatedAt DESC";
                using var cmd = new SqlCommand(query, conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    alerts.Add(new
                    {
                        AlertID = reader.GetInt32(0),
                        AlertType = reader.GetString(1),
                        Description = reader.GetString(2),
                        CreatedAt = reader.GetDateTime(3).ToString("hh:mm tt"),
                        RiskLevel = reader.GetString(4),
                        CashierName = reader.GetString(5)
                    });
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
            return Json(alerts);
        }

        // ========== DISMISS FRAUD ALERT ==========
        [HttpPost]
        public IActionResult DismissAlert(int id)
        {
            try
            {
                using var conn = _db.GetConnection();
                conn.Open();
                string query = "UPDATE FraudAlerts SET Status = 'Dismissed' WHERE AlertID = @id";
                using var cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }
    }
}