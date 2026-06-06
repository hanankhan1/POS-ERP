using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using POSERP.Models.DataBase;
using System;
using System.Collections.Generic;
using System.Security.Claims;

namespace POSERP.Controllers.Cashier
{
    [Authorize(Roles = "Cashier")]
    public class CashierController : Controller
    {
        private readonly Db _db;
        public CashierController(Db db) => _db = db;

        // DASHBOARD – uses separate connections to avoid DataReader conflict
        public IActionResult Dashboard()
        {
            int userId = GetCurrentUserId();

            // Query 1: Today's sales (separate connection)
            decimal todaySales = 0;
            int todayTransactions = 0;
            using (var conn = _db.GetConnection())
            {
                conn.Open();
                string query = @"
                    SELECT ISNULL(SUM(TotalAmount),0), COUNT(*)
                    FROM Sales
                    WHERE CashierID = @uid AND CAST(SaleDate AS DATE) = CAST(GETDATE() AS DATE) AND IsCancelled = 0";
                using var cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@uid", userId);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    todaySales = reader.GetDecimal(0);
                    todayTransactions = reader.GetInt32(1);
                }
            }

            ViewBag.TodaySales = todaySales;
            ViewBag.TodayTransactions = todayTransactions;

            // Query 2: Clock status (separate connection)
            bool hasClockedIn = false;
            bool hasClockedOut = false;
            using (var conn2 = _db.GetConnection())
            {
                conn2.Open();
                string checkClock = @"
                    SELECT TOP 1 CheckInTime, CheckOutTime 
                    FROM Attendance a
                    JOIN Employees e ON a.EmployeeID = e.EmployeeID
                    WHERE e.UserID = @uid AND a.AttendanceDate = CAST(GETDATE() AS DATE)
                    ORDER BY a.AttendanceID DESC";
                using var cmdClock = new SqlCommand(checkClock, conn2);
                cmdClock.Parameters.AddWithValue("@uid", userId);
                using var clockReader = cmdClock.ExecuteReader();
                if (clockReader.Read())
                {
                    hasClockedIn = !clockReader.IsDBNull(0);
                    hasClockedOut = !clockReader.IsDBNull(1);
                }
            }

            ViewBag.HasClockedIn = hasClockedIn;
            ViewBag.HasClockedOut = hasClockedOut;

            return View();
        }

        [HttpPost]
        public IActionResult ClockIn()
        {
            int userId = GetCurrentUserId();
            int employeeId = GetEmployeeId(userId);
            if (employeeId == 0)
                return Json(new { success = false, message = "Employee record not found." });

            using var conn = _db.GetConnection();
            conn.Open();
            string checkQuery = @"
                SELECT COUNT(*) FROM Attendance 
                WHERE EmployeeID = @empId AND AttendanceDate = CAST(GETDATE() AS DATE) AND CheckOutTime IS NULL";
            using var cmdCheck = new SqlCommand(checkQuery, conn);
            cmdCheck.Parameters.AddWithValue("@empId", employeeId);
            int openRecord = (int)cmdCheck.ExecuteScalar();
            if (openRecord > 0)
                return Json(new { success = false, message = "Already clocked in today. Please clock out first." });

            string insertQuery = @"
                INSERT INTO Attendance (EmployeeID, AttendanceDate, CheckInTime, Status)
                VALUES (@empId, CAST(GETDATE() AS DATE), CAST(GETDATE() AS TIME), 'Present')";
            using var cmdInsert = new SqlCommand(insertQuery, conn);
            cmdInsert.Parameters.AddWithValue("@empId", employeeId);
            cmdInsert.ExecuteNonQuery();
            return Json(new { success = true, message = "Clocked in successfully." });
        }

        [HttpPost]
        public IActionResult ClockOut()
        {
            int userId = GetCurrentUserId();
            int employeeId = GetEmployeeId(userId);
            if (employeeId == 0)
                return Json(new { success = false, message = "Employee record not found." });

            using var conn = _db.GetConnection();
            conn.Open();
            string updateQuery = @"
                UPDATE Attendance 
                SET CheckOutTime = CAST(GETDATE() AS TIME)
                WHERE EmployeeID = @empId AND AttendanceDate = CAST(GETDATE() AS DATE) AND CheckOutTime IS NULL";
            using var cmd = new SqlCommand(updateQuery, conn);
            cmd.Parameters.AddWithValue("@empId", employeeId);
            int rows = cmd.ExecuteNonQuery();
            if (rows == 0)
                return Json(new { success = false, message = "No open clock-in record found for today." });
            return Json(new { success = true, message = "Clocked out successfully." });
        }

        public IActionResult MySales(DateTime? from, DateTime? to)
        {
            int userId = GetCurrentUserId();
            DateTime start = from ?? DateTime.Today.AddDays(-30);
            // Set end to 23:59:59 of the selected day (or today)
            DateTime end = to ?? DateTime.Today;
            end = end.Date.AddDays(1).AddSeconds(-1); // end of the day

            ViewBag.StartDate = start.ToString("yyyy-MM-dd");
            ViewBag.EndDate = (to ?? DateTime.Today).ToString("yyyy-MM-dd");

            var sales = new List<dynamic>();
            using var conn = _db.GetConnection();
            conn.Open();

            // Main query with inclusive date range
            string query = @"
    SELECT SaleID, SaleDate, TotalAmount, DiscountAmount, TaxAmount, PaymentMethod, PaymentStatus, IsCancelled
    FROM Sales
    WHERE CashierID = @uid
    AND CAST(SaleDate AS DATE) BETWEEN CAST(@from AS DATE) AND CAST(@to AS DATE)
    ORDER BY SaleDate DESC";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@from", start);
            cmd.Parameters.AddWithValue("@to", end);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                sales.Add(new
                {
                    SaleID = reader.GetInt32(0),
                    SaleDate = reader.GetDateTime(1),
                    TotalAmount = reader.GetDecimal(2),
                    DiscountAmount = reader.GetDecimal(3),
                    TaxAmount = reader.GetDecimal(4),
                    PaymentMethod = reader.GetString(5),
                    PaymentStatus = reader.GetString(6),
                    IsCancelled = reader.GetBoolean(7)
                });
            }
            return View(sales);
        }

        public IActionResult Receipt(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                SELECT ReceiptNumber, ReceiptHTML, GeneratedAt
                FROM Receipts
                WHERE SaleID = @sid";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@sid", id);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                ViewBag.ReceiptHTML = reader.GetString(1);
                ViewBag.ReceiptNumber = reader.GetString(0);
                ViewBag.GeneratedAt = reader.GetDateTime(2);
                return View();
            }
            return RedirectToAction("MySales");
        }

        private int GetCurrentUserId()
        {
            string email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.Identity?.Name;
            if (string.IsNullOrEmpty(email)) return 0;

            using var conn = _db.GetConnection();
            conn.Open();
            string query = "SELECT UserID FROM Users WHERE Email = @email";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@email", email);
            var result = cmd.ExecuteScalar();
            if (result != null && int.TryParse(result.ToString(), out int uid))
                return uid;
            return 0;
        }

        private int GetEmployeeId(int userId)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string query = "SELECT EmployeeID FROM Employees WHERE UserID = @uid";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@uid", userId);
            var result = cmd.ExecuteScalar();
            return result != null ? Convert.ToInt32(result) : 0;
        }
    }
}