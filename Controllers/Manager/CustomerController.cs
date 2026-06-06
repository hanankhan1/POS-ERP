using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using POSERP.Models.DataBase;
using POSERP.Models.Customer;
using System;
using System.Collections.Generic;
using System.Security.Claims;

namespace POSERP.Controllers.Manager
{
    [Authorize(Roles = "Manager,Admin")]
    public class CustomerController : Controller
    {
        private readonly Db _db;

        public CustomerController(Db db)
        {
            _db = db;
        }

        // ========== HELPER METHODS ==========
        private int GetCurrentUserId()
        {
            var claim = User.FindFirst("UserID");
            return claim != null ? int.Parse(claim.Value) : 0;
        }

        private void LogAction(int userId, string action, string tableName, int recordId)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string sql = "INSERT INTO AuditLogs (UserID, Action, TableName, RecordID) VALUES (@uid, @act, @tbl, @rid)";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@act", action);
            cmd.Parameters.AddWithValue("@tbl", tableName);
            cmd.Parameters.AddWithValue("@rid", recordId);
            cmd.ExecuteNonQuery();
        }

        // ========== LIST CUSTOMERS (with search & show inactive toggle) ==========
        public IActionResult CustomerList(string search, bool showInactive = false)
        {
            var customers = new List<CustomerViewModel>();
            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                SELECT CustomerID, FullName, Phone, Address, LoyaltyPoints, CreditBalance, IsActive
                FROM Customers
                WHERE (@showInactive = 1 OR IsActive = 1)
                  AND (@search IS NULL OR FullName LIKE @search OR Phone LIKE @search)
                ORDER BY FullName";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@showInactive", showInactive ? 1 : 0);
            if (!string.IsNullOrEmpty(search))
                cmd.Parameters.AddWithValue("@search", $"%{search}%");
            else
                cmd.Parameters.AddWithValue("@search", DBNull.Value);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                customers.Add(new CustomerViewModel
                {
                    CustomerID = reader.GetInt32(0),
                    FullName = reader.GetString(1),
                    Phone = reader.GetString(2),
                    Address = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    LoyaltyPoints = reader.GetInt32(4),
                    CreditBalance = reader.GetDecimal(5),
                    IsActive = reader.GetBoolean(6)
                });
            }
            ViewBag.SearchTerm = search;
            ViewBag.ShowInactive = showInactive;
            return View(customers);
        }

        // ========== ADD CUSTOMER ==========
        [HttpGet]
        public IActionResult Add() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Add(CustomerViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                INSERT INTO Customers (FullName, Phone, Address, LoyaltyPoints, CreditBalance, IsActive)
                VALUES (@FullName, @Phone, @Address, @LoyaltyPoints, @CreditBalance, 1);
                SELECT SCOPE_IDENTITY();";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@FullName", model.FullName);
            cmd.Parameters.AddWithValue("@Phone", model.Phone);
            cmd.Parameters.AddWithValue("@Address", model.Address ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@LoyaltyPoints", model.LoyaltyPoints);
            cmd.Parameters.AddWithValue("@CreditBalance", model.CreditBalance);
            int newId = Convert.ToInt32(cmd.ExecuteScalar());

            LogAction(GetCurrentUserId(), "INSERT", "Customers", newId);
            TempData["Success"] = "Customer added successfully.";
            return RedirectToAction("CustomerList");
        }

        // ========== EDIT CUSTOMER ==========
        [HttpGet]
        public IActionResult Edit(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string query = "SELECT CustomerID, FullName, Phone, Address, LoyaltyPoints, CreditBalance FROM Customers WHERE CustomerID = @id AND IsActive = 1";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var model = new CustomerViewModel
                {
                    CustomerID = reader.GetInt32(0),
                    FullName = reader.GetString(1),
                    Phone = reader.GetString(2),
                    Address = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    LoyaltyPoints = reader.GetInt32(4),
                    CreditBalance = reader.GetDecimal(5)
                };
                return View(model);
            }
            return NotFound();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(CustomerViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                UPDATE Customers 
                SET FullName=@FullName, Phone=@Phone, Address=@Address, 
                    LoyaltyPoints=@LoyaltyPoints, CreditBalance=@CreditBalance
                WHERE CustomerID=@CustomerID AND IsActive=1";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@CustomerID", model.CustomerID);
            cmd.Parameters.AddWithValue("@FullName", model.FullName);
            cmd.Parameters.AddWithValue("@Phone", model.Phone);
            cmd.Parameters.AddWithValue("@Address", model.Address ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@LoyaltyPoints", model.LoyaltyPoints);
            cmd.Parameters.AddWithValue("@CreditBalance", model.CreditBalance);
            cmd.ExecuteNonQuery();

            LogAction(GetCurrentUserId(), "UPDATE", "Customers", model.CustomerID);
            TempData["Success"] = "Customer updated successfully.";
            return RedirectToAction("CustomerList");
        }

        // ========== DELETE (SOFT DELETE) ==========
        [HttpPost]
        public IActionResult Delete(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            // Check if customer has sales
            string checkSales = "SELECT COUNT(*) FROM Sales WHERE CustomerID = @id";
            using var cmd = new SqlCommand(checkSales, conn);
            cmd.Parameters.AddWithValue("@id", id);
            int salesCount = (int)cmd.ExecuteScalar();
            if (salesCount > 0)
                return Json(new { success = false, message = "Cannot delete customer with existing sales. Deactivate instead." });

            // Soft delete
            string softDelete = "UPDATE Customers SET IsActive = 0 WHERE CustomerID = @id";
            using var delCmd = new SqlCommand(softDelete, conn);
            delCmd.Parameters.AddWithValue("@id", id);
            delCmd.ExecuteNonQuery();

            LogAction(GetCurrentUserId(), "SOFT_DELETE", "Customers", id);
            return Json(new { success = true });
        }

        // ========== LOYALTY & CREDIT MANAGEMENT ==========
        [HttpGet]
        public IActionResult Loyalty(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string query = "SELECT CustomerID, FullName, LoyaltyPoints, CreditBalance FROM Customers WHERE CustomerID = @id AND IsActive = 1";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var model = new CustomerViewModel
                {
                    CustomerID = reader.GetInt32(0),
                    FullName = reader.GetString(1),
                    LoyaltyPoints = reader.GetInt32(2),
                    CreditBalance = reader.GetDecimal(3)
                };
                return View(model);
            }
            return NotFound();
        }

        [HttpPost]
        public IActionResult UpdateLoyalty(int customerId, int pointsToAdd, decimal creditToAdd)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                UPDATE Customers 
                SET LoyaltyPoints = LoyaltyPoints + @points,
                    CreditBalance = CreditBalance + @credit
                WHERE CustomerID = @id AND IsActive = 1";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@points", pointsToAdd);
            cmd.Parameters.AddWithValue("@credit", creditToAdd);
            cmd.Parameters.AddWithValue("@id", customerId);
            cmd.ExecuteNonQuery();

            LogAction(GetCurrentUserId(), "UPDATE_LOYALTY", "Customers", customerId);
            return RedirectToAction("CustomerList");
        }

        // ========== PURCHASE HISTORY ==========
        [HttpGet]
        public IActionResult PurchaseHistory(int id)
        {
            var model = new CustomerPurchaseHistoryViewModel();
            using var conn = _db.GetConnection();
            conn.Open();
            // Customer info
            string custQuery = "SELECT CustomerID, FullName, LoyaltyPoints, CreditBalance FROM Customers WHERE CustomerID = @id";
            using var cmd = new SqlCommand(custQuery, conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return NotFound();
            model.CustomerID = reader.GetInt32(0);
            model.CustomerName = reader.GetString(1);
            model.LoyaltyPoints = reader.GetInt32(2);
            model.CreditBalance = reader.GetDecimal(3);
            reader.Close();

            // Sales with items
            string salesQuery = @"
                SELECT s.SaleID, s.SaleDate, s.TotalAmount, s.DiscountAmount, s.TaxAmount, 
                       s.PaymentMethod, s.PaymentStatus, s.IsCancelled,
                       si.ProductID, p.ProductName, si.Quantity, si.UnitPrice, si.SubTotal
                FROM Sales s
                LEFT JOIN SaleItems si ON s.SaleID = si.SaleID
                LEFT JOIN Products p ON si.ProductID = p.ProductID
                WHERE s.CustomerID = @id
                ORDER BY s.SaleDate DESC";
            using var cmdSales = new SqlCommand(salesQuery, conn);
            cmdSales.Parameters.AddWithValue("@id", id);
            using var salesReader = cmdSales.ExecuteReader();
            var salesDict = new Dictionary<int, CustomerSale>();
            while (salesReader.Read())
            {
                int saleId = salesReader.GetInt32(0);
                if (!salesDict.ContainsKey(saleId))
                {
                    salesDict[saleId] = new CustomerSale
                    {
                        SaleId = saleId,
                        SaleDate = salesReader.GetDateTime(1),
                        TotalAmount = salesReader.GetDecimal(2),
                        DiscountAmount = salesReader.GetDecimal(3),
                        TaxAmount = salesReader.GetDecimal(4),
                        PaymentMethod = salesReader.GetString(5),
                        PaymentStatus = salesReader.GetString(6),
                        IsCancelled = salesReader.GetBoolean(7),
                        Items = new List<SaleItemInfo>()
                    };
                }
                if (!salesReader.IsDBNull(8))
                {
                    salesDict[saleId].Items.Add(new SaleItemInfo
                    {
                        ProductName = salesReader.GetString(9),
                        Quantity = salesReader.GetInt32(10),
                        UnitPrice = salesReader.GetDecimal(11),
                        SubTotal = salesReader.GetDecimal(12)
                    });
                }
            }
            model.Sales = salesDict.Values.ToList();
            return View(model);
        }

        // ========== CUSTOMER ACTIVITY LOG (AUDIT) ==========
        [HttpGet]
        public IActionResult CustomerLogs(string search)
        {
            var logs = new List<dynamic>();
            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                SELECT l.LogID, l.Action, l.TableName, l.RecordID, l.Timestamp, u.FullName AS UserName
                FROM AuditLogs l
                JOIN Users u ON l.UserID = u.UserID
                WHERE l.TableName = 'Customers'
                  AND (@search IS NULL OR CAST(l.RecordID AS NVARCHAR) LIKE @search)
                ORDER BY l.Timestamp DESC";
            using var cmd = new SqlCommand(query, conn);
            if (!string.IsNullOrEmpty(search))
                cmd.Parameters.AddWithValue("@search", $"%{search}%");
            else
                cmd.Parameters.AddWithValue("@search", DBNull.Value);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                logs.Add(new
                {
                    LogID = reader.GetInt32(0),
                    Action = reader.GetString(1),
                    TableName = reader.GetString(2),
                    RecordID = reader.GetInt32(3),
                    Timestamp = reader.GetDateTime(4),
                    UserName = reader.GetString(5)
                });
            }
            ViewBag.SearchTerm = search;
            return View(logs);
        }
    }
}