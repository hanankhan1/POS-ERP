using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using POSERP.Models.DataBase;
using POSERP.Models.Supplier;
using POSERP.Models.ViewModels;
using System;
using System.Collections.Generic;

namespace POSERP.Controllers.Manager
{
    [Authorize(Roles = "Manager,Admin")]
    public class SupplierController : Controller
    {
        private readonly Db _db;
        public SupplierController(Db db) => _db = db;

        // ========== LIST SUPPLIERS (with search & inactive toggle) ==========
        public IActionResult Index(string search, bool showInactive = false)
        {
            var suppliers = new List<SupplierViewModel>();
            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                SELECT SupplierID, SupplierName, ContactPerson, Phone, Email, Address, IsActive
                FROM Suppliers
                WHERE (@showInactive = 1 OR IsActive = 1)
                  AND (@search IS NULL OR SupplierName LIKE @search OR ContactPerson LIKE @search OR Phone LIKE @search)
                ORDER BY SupplierName";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@showInactive", showInactive ? 1 : 0);
            if (!string.IsNullOrEmpty(search))
                cmd.Parameters.AddWithValue("@search", $"%{search}%");
            else
                cmd.Parameters.AddWithValue("@search", DBNull.Value);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                suppliers.Add(new SupplierViewModel
                {
                    SupplierID = reader.GetInt32(0),
                    SupplierName = reader.GetString(1),
                    ContactPerson = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Phone = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Email = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Address = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    IsActive = reader.GetBoolean(6)
                });
            }
            ViewBag.SearchTerm = search;
            ViewBag.ShowInactive = showInactive;
            return View(suppliers);
        }

        // ========== ADD SUPPLIER ==========
        [HttpGet]
        public IActionResult AddSupplier() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddSupplier(SupplierViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                INSERT INTO Suppliers (SupplierName, ContactPerson, Phone, Email, Address, IsActive)
                VALUES (@Name, @Contact, @Phone, @Email, @Address, 1);
                SELECT SCOPE_IDENTITY();";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Name", model.SupplierName);
            cmd.Parameters.AddWithValue("@Contact", model.ContactPerson ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Phone", model.Phone ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Email", model.Email ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Address", model.Address ?? (object)DBNull.Value);
            int newId = Convert.ToInt32(cmd.ExecuteScalar());

            TempData["Success"] = "Supplier added successfully.";
            return RedirectToAction("Index");
        }
        // In SupplierController.cs
        // ========== SUPPLIER PRODUCTS ==========
        // ========== SUPPLIER PRODUCTS ==========
        public IActionResult SupplierProducts(int id)
        {
            var products = new List<SupplierProductViewModel>();

            // First, get products using its own connection
            using (var conn = _db.GetConnection())
            {
                conn.Open();
                string query = @"
            SELECT p.ProductID, p.ProductName, p.Barcode, p.SellingPrice, 
                   ISNULL(i.QuantityInStock, 0) AS StockQuantity
            FROM Products p
            LEFT JOIN Inventory i ON p.ProductID = i.ProductID
            WHERE p.SupplierID = @id AND p.IsActive = 1
            ORDER BY p.ProductName";
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            products.Add(new SupplierProductViewModel
                            {
                                ProductID = reader.GetInt32(0),
                                ProductName = reader.GetString(1),
                                Barcode = reader.GetString(2),
                                SellingPrice = reader.GetDecimal(3),
                                StockQuantity = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
                            });
                        }
                    }
                }
            }

            // Second, get supplier info using a separate connection
            using (var conn2 = _db.GetConnection())
            {
                conn2.Open();
                string infoQuery = "SELECT SupplierName, IsActive FROM Suppliers WHERE SupplierID = @id";
                using (var cmd = new SqlCommand(infoQuery, conn2))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            ViewBag.SupplierName = reader.GetString(0);
                            ViewBag.SupplierIsActive = reader.GetBoolean(1);
                        }
                        else
                        {
                            ViewBag.SupplierName = "Unknown";
                            ViewBag.SupplierIsActive = false;
                        }
                    }
                }
            }

            return View(products);
        }
        // ========== EDIT SUPPLIER ==========
        [HttpGet]
        public IActionResult EditSupplier(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string query = "SELECT SupplierID, SupplierName, ContactPerson, Phone, Email, Address, IsActive FROM Suppliers WHERE SupplierID = @id";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var model = new SupplierViewModel
                {
                    SupplierID = reader.GetInt32(0),
                    SupplierName = reader.GetString(1),
                    ContactPerson = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Phone = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Email = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Address = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    IsActive = reader.GetBoolean(6)
                };
                return View(model);
            }
            return NotFound();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditSupplier(SupplierViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                UPDATE Suppliers 
                SET SupplierName = @Name, ContactPerson = @Contact, Phone = @Phone, Email = @Email, Address = @Address
                WHERE SupplierID = @id";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@id", model.SupplierID);
            cmd.Parameters.AddWithValue("@Name", model.SupplierName);
            cmd.Parameters.AddWithValue("@Contact", model.ContactPerson ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Phone", model.Phone ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Email", model.Email ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Address", model.Address ?? (object)DBNull.Value);
            cmd.ExecuteNonQuery();

            TempData["Success"] = "Supplier updated.";
            return RedirectToAction("Index");
        }

        // ========== DELETE (SOFT DELETE) ==========
        
        [HttpPost]
        public IActionResult DeactivateSupplier(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var transaction = conn.BeginTransaction();

            try
            {
                // Deactivate supplier
                string supplierQuery = @"
            UPDATE Suppliers
            SET IsActive = 0
            WHERE SupplierID = @id";

                using var supplierCmd = new SqlCommand(supplierQuery, conn, transaction);
                supplierCmd.Parameters.AddWithValue("@id", id);
                supplierCmd.ExecuteNonQuery();

                // Deactivate all supplier products
                string productQuery = @"
            UPDATE Products
            SET IsActive = 0
            WHERE SupplierID = @id";

                using var productCmd = new SqlCommand(productQuery, conn, transaction);
                productCmd.Parameters.AddWithValue("@id", id);
                productCmd.ExecuteNonQuery();

                transaction.Commit();

                return Json(new
                {
                    success = true,
                    message = "Supplier and related products deactivated successfully."
                });
            }
            catch (Exception ex)
            {
                transaction.Rollback();

                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }
        [HttpPost]
        public IActionResult ReactivateSupplier(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var transaction = conn.BeginTransaction();

            try
            {
                // Reactivate supplier
                string supplierQuery = @"
            UPDATE Suppliers
            SET IsActive = 1
            WHERE SupplierID = @id";

                using var supplierCmd = new SqlCommand(supplierQuery, conn, transaction);
                supplierCmd.Parameters.AddWithValue("@id", id);
                supplierCmd.ExecuteNonQuery();

                // Reactivate products
                string productQuery = @"
            UPDATE Products
            SET IsActive = 1
            WHERE SupplierID = @id";

                using var productCmd = new SqlCommand(productQuery, conn, transaction);
                productCmd.Parameters.AddWithValue("@id", id);
                productCmd.ExecuteNonQuery();

                transaction.Commit();

                return Json(new
                {
                    success = true,
                    message = "Supplier and products reactivated successfully."
                });
            }
            catch (Exception ex)
            {
                transaction.Rollback();

                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // ========== SUPPLIER TRANSACTIONS (Purchase Orders) ==========
        public IActionResult SupplierTransactions(int id)
        {
            var transactions = new List<SupplierTransactionViewModel>();
            using var conn = _db.GetConnection();
            conn.Open();

            // Get supplier info
            string supQuery = "SELECT SupplierName FROM Suppliers WHERE SupplierID = @id";
            using var supCmd = new SqlCommand(supQuery, conn);
            supCmd.Parameters.AddWithValue("@id", id);
            ViewBag.SupplierName = supCmd.ExecuteScalar()?.ToString() ?? "Unknown";

            // Get purchase orders
            string poQuery = @"
                SELECT po.PurchaseOrderID, po.OrderDate, po.TotalAmount, po.Status,
                       (SELECT COUNT(*) FROM PurchaseOrderItems WHERE PurchaseOrderID = po.PurchaseOrderID) AS ItemCount
                FROM PurchaseOrders po
                WHERE po.SupplierID = @id
                ORDER BY po.OrderDate DESC";
            using var cmd = new SqlCommand(poQuery, conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                transactions.Add(new SupplierTransactionViewModel
                {
                    PurchaseOrderID = reader.GetInt32(0),
                    OrderDate = reader.GetDateTime(1),
                    TotalAmount = reader.GetDecimal(2),
                    Status = reader.GetString(3),
                    ItemCount = reader.GetInt32(4)
                });
            }
            return View(transactions);
        }
    }
}