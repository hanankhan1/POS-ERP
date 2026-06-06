using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using POSERP.Models.DataBase;
using System;
using System.Collections.Generic;

namespace POSERP.Controllers.Manager
{
    [Authorize(Roles = "Manager")]
    public class ManagerController : Controller
    {
        private readonly Db _db;

        public ManagerController(Db db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();

                // Total Products
                string totalQuery = "SELECT COUNT(*) FROM Products WHERE IsActive = 1";
                ViewBag.TotalProducts = (int)new SqlCommand(totalQuery, con).ExecuteScalar();

                // Low Stock (QuantityInStock > 0 AND QuantityInStock <= ReorderLevel)
                string lowStockQuery = @"
                    SELECT COUNT(*) 
                    FROM Products p
                    INNER JOIN Inventory i ON p.ProductID = i.ProductID
                    WHERE i.QuantityInStock > 0 AND i.QuantityInStock <= p.ReorderLevel";
                ViewBag.LowStock = (int)new SqlCommand(lowStockQuery, con).ExecuteScalar();

                // Out of Stock
                string outStockQuery = @"
                    SELECT COUNT(*) 
                    FROM Products p
                    INNER JOIN Inventory i ON p.ProductID = i.ProductID
                    WHERE i.QuantityInStock = 0";
                ViewBag.OutOfStock = (int)new SqlCommand(outStockQuery, con).ExecuteScalar();

                // Today's Sales
                string todaySalesQuery = @"
                    SELECT ISNULL(SUM(TotalAmount), 0) 
                    FROM Sales 
                    WHERE PaymentStatus = 'Paid' 
                    AND CAST(SaleDate AS DATE) = CAST(GETDATE() AS DATE)";
                ViewBag.TodaySales = Convert.ToDecimal(new SqlCommand(todaySalesQuery, con).ExecuteScalar()).ToString("F2");

                // Sales trend (last 7 days)
                var salesLabels = new List<string>();
                var salesValues = new List<decimal>();
                string trendQuery = @"
                    SELECT 
                        FORMAT(SaleDate, 'ddd') AS DayName,
                        ISNULL(SUM(TotalAmount), 0) AS DailyTotal
                    FROM Sales
                    WHERE PaymentStatus = 'Paid' 
                      AND SaleDate >= DATEADD(day, -6, CAST(GETDATE() AS DATE))
                    GROUP BY FORMAT(SaleDate, 'ddd'), CAST(SaleDate AS DATE)
                    ORDER BY MIN(SaleDate)";
                var cmdTrend = new SqlCommand(trendQuery, con);
                using (var reader = cmdTrend.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        salesLabels.Add(reader["DayName"].ToString());
                        salesValues.Add(Convert.ToDecimal(reader["DailyTotal"]));
                    }
                }
                ViewBag.SalesLabels = salesLabels;
                ViewBag.SalesValues = salesValues;

                // Recent Orders (top 5)
                string recentQuery = @"
                    SELECT TOP 5 
                        s.SaleID AS OrderId,
                        ISNULL(c.FullName, 'Walk-in Customer') AS CustomerName,
                        s.TotalAmount AS Amount,
                        s.PaymentStatus AS Status
                    FROM Sales s
                    LEFT JOIN Customers c ON s.CustomerID = c.CustomerID
                    ORDER BY s.SaleDate DESC";
                var recentOrders = new List<dynamic>();
                var cmdRecent = new SqlCommand(recentQuery, con);
                using (var reader = cmdRecent.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        recentOrders.Add(new
                        {
                            OrderId = reader["OrderId"].ToString(),
                            CustomerName = reader["CustomerName"].ToString(),
                            Amount = Convert.ToDecimal(reader["Amount"]).ToString("F2"),
                            Status = reader["Status"].ToString()
                        });
                    }
                }
                ViewBag.RecentOrders = recentOrders;
            }
            return View("Dashboard");
        }

        [HttpPost]
        public IActionResult ClockIn()
        {
            int userId = Convert.ToInt32(User.FindFirst("UserID")?.Value);
            int employeeId = EnsureEmployeeExists(userId);
            if (employeeId == 0)
                return Json(new { success = false, message = "Could not create employee record. Contact admin." });

            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();
                // Check if already clocked in today without checkout
                string checkQuery = @"
                    SELECT COUNT(*) FROM Attendance 
                    WHERE EmployeeID = @empId 
                    AND AttendanceDate = CAST(GETDATE() AS DATE) 
                    AND CheckOutTime IS NULL";
                var checkCmd = new SqlCommand(checkQuery, con);
                checkCmd.Parameters.AddWithValue("@empId", employeeId);
                int openRecord = (int)checkCmd.ExecuteScalar();
                if (openRecord > 0)
                    return Json(new { success = false, message = "Already clocked in today. Please clock out first." });

                string insertQuery = @"
                    INSERT INTO Attendance (EmployeeID, AttendanceDate, CheckInTime, Status)
                    VALUES (@empId, CAST(GETDATE() AS DATE), CAST(GETDATE() AS TIME), 'Present')";
                var insertCmd = new SqlCommand(insertQuery, con);
                insertCmd.Parameters.AddWithValue("@empId", employeeId);
                insertCmd.ExecuteNonQuery();
            }
            return Json(new { success = true, message = "Clocked in successfully." });
        }

        [HttpPost]
        public IActionResult ClockOut()
        {
            int userId = Convert.ToInt32(User.FindFirst("UserID")?.Value);
            int employeeId = EnsureEmployeeExists(userId);
            if (employeeId == 0)
                return Json(new { success = false, message = "Could not create employee record." });

            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();
                string updateQuery = @"
                    UPDATE Attendance 
                    SET CheckOutTime = CAST(GETDATE() AS TIME)
                    WHERE EmployeeID = @empId 
                      AND AttendanceDate = CAST(GETDATE() AS DATE)
                      AND CheckOutTime IS NULL";
                var updateCmd = new SqlCommand(updateQuery, con);
                updateCmd.Parameters.AddWithValue("@empId", employeeId);
                int rows = updateCmd.ExecuteNonQuery();
                if (rows == 0)
                    return Json(new { success = false, message = "No open clock-in record found for today." });
            }
            return Json(new { success = true, message = "Clocked out successfully." });
        }

        // Auto-create employee record if missing (for non-Admin users)
        private int EnsureEmployeeExists(int userId)
        {
            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();
                // Check if employee already exists
                string checkQuery = "SELECT EmployeeID FROM Employees WHERE UserID = @userId";
                var checkCmd = new SqlCommand(checkQuery, con);
                checkCmd.Parameters.AddWithValue("@userId", userId);
                var existingId = checkCmd.ExecuteScalar();
                if (existingId != null)
                    return Convert.ToInt32(existingId);

                // Get user role and name to create employee
                string userQuery = "SELECT Role, FullName FROM Users WHERE UserID = @userId";
                var userCmd = new SqlCommand(userQuery, con);
                userCmd.Parameters.AddWithValue("@userId", userId);
                using (var reader = userCmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        string role = reader["Role"].ToString();
                        string fullName = reader["FullName"].ToString();

                        // Only create for non-Admin roles (Manager, Cashier, Employee)
                        if (role != "Admin")
                        {
                            string tempCNIC = "AUTO_" + userId.ToString();
                            string insertQuery = @"
                                INSERT INTO Employees (UserID, CNIC, HireDate, Salary, Designation)
                                VALUES (@uid, @cnic, @hireDate, @salary, @designation);
                                SELECT SCOPE_IDENTITY();";
                            var insertCmd = new SqlCommand(insertQuery, con);
                            insertCmd.Parameters.AddWithValue("@uid", userId);
                            insertCmd.Parameters.AddWithValue("@cnic", tempCNIC);
                            insertCmd.Parameters.AddWithValue("@hireDate", DateTime.Now);
                            insertCmd.Parameters.AddWithValue("@salary", 0);
                            insertCmd.Parameters.AddWithValue("@designation", role);
                            int newEmployeeId = Convert.ToInt32(insertCmd.ExecuteScalar());
                            return newEmployeeId;
                        }
                    }
                }
                return 0; // Admin or error
            }
        }
    }
}