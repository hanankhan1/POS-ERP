using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using POSERP.Models.DataBase;
using System;
using System.Collections.Generic;
using System.Data;

namespace POSERP.Controllers.Manager
{
    [Authorize(Roles = "Manager,Admin")]
    public class ProductController : Controller
    {
        private readonly Db _db;
        public ProductController(Db db) => _db = db;

        // ===============================
        // 1. PRODUCT LIST
        // ===============================
        public IActionResult Product(string search = "")
        {
            var products = new List<dynamic>();
            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();
                string query = @"
            SELECT p.ProductID, p.ProductName, p.Barcode, p.CostPrice, p.SellingPrice, 
                   p.ReorderLevel, p.Unit, p.Status, p.IsActive,
                   c.CategoryName, s.SupplierName,
                   ISNULL(i.QuantityInStock, 0) AS QuantityInStock
            FROM Products p
            LEFT JOIN Categories c ON p.CategoryID = c.CategoryID
            LEFT JOIN Suppliers s ON p.SupplierID = s.SupplierID
            LEFT JOIN Inventory i ON p.ProductID = i.ProductID
            WHERE p.IsActive = 1";

                if (!string.IsNullOrEmpty(search))
                {
                    query += " AND (p.ProductName LIKE @search OR p.Barcode LIKE @search OR c.CategoryName LIKE @search OR s.SupplierName LIKE @search)";
                }

                var cmd = new SqlCommand(query, con);
                if (!string.IsNullOrEmpty(search))
                {
                    cmd.Parameters.AddWithValue("@search", "%" + search + "%");
                }

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        products.Add(new
                        {
                            ProductID = reader["ProductID"],
                            ProductName = reader["ProductName"],
                            Barcode = reader["Barcode"],
                            CostPrice = reader["CostPrice"],
                            SellingPrice = reader["SellingPrice"],
                            ReorderLevel = reader["ReorderLevel"],
                            Unit = reader["Unit"],
                            Status = reader["Status"],
                            CategoryName = reader["CategoryName"],
                            SupplierName = reader["SupplierName"],
                            QuantityInStock = reader["QuantityInStock"]
                        });
                    }
                }
            }
            ViewBag.Search = search;
            return View(products);
        }
        // ===============================
        // 2. ADD PRODUCT (GET)
        // ===============================
        public IActionResult Add()
        {
            LoadCategoriesAndSuppliers();
            return View();
        }

        // ===============================
        // 3. ADD PRODUCT (POST)
        // ===============================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Add(IFormCollection form)
        {
            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();
                using (var transaction = con.BeginTransaction())
                {
                    try
                    {
                        // Insert Product
                        string productQuery = @"
                            INSERT INTO Products (CategoryID, SupplierID, ProductName, Barcode, 
                                                 CostPrice, SellingPrice, ReorderLevel, Unit, Status, IsActive)
                            VALUES (@cat, @sup, @name, @barcode, @cost, @sell, @reorder, @unit, 'Available', 1);
                            SELECT SCOPE_IDENTITY();";
                        var prodCmd = new SqlCommand(productQuery, con, transaction);
                        prodCmd.Parameters.AddWithValue("@cat", int.Parse(form["CategoryID"]));
                        prodCmd.Parameters.AddWithValue("@sup", int.Parse(form["SupplierID"]));
                        prodCmd.Parameters.AddWithValue("@name", form["ProductName"].ToString());
                        prodCmd.Parameters.AddWithValue("@barcode", form["Barcode"].ToString());
                        prodCmd.Parameters.AddWithValue("@cost", decimal.Parse(form["CostPrice"]));
                        prodCmd.Parameters.AddWithValue("@sell", decimal.Parse(form["SellingPrice"]));
                        prodCmd.Parameters.AddWithValue("@reorder", int.Parse(form["ReorderLevel"]));
                        prodCmd.Parameters.AddWithValue("@unit", form["Unit"].ToString());
                        int newProductId = Convert.ToInt32(prodCmd.ExecuteScalar());

                        // Insert Inventory record (initial stock 0)
                        string invQuery = "INSERT INTO Inventory (ProductID, QuantityInStock) VALUES (@pid, 0)";
                        var invCmd = new SqlCommand(invQuery, con, transaction);
                        invCmd.Parameters.AddWithValue("@pid", newProductId);
                        invCmd.ExecuteNonQuery();

                        transaction.Commit();
                        TempData["Success"] = "Product added successfully.";
                        return RedirectToAction("Product");
                    }
                    catch
                    {
                        transaction.Rollback();
                        TempData["Error"] = "Error adding product.";
                        LoadCategoriesAndSuppliers();
                        return View();
                    }
                }
            }
        }

        // ===============================
        // 4. EDIT PRODUCT (GET)
        // ===============================
        public IActionResult Edit(int id)
        {
            dynamic product = null;
            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();
                string query = @"
                    SELECT ProductID, CategoryID, SupplierID, ProductName, Barcode,
                           CostPrice, SellingPrice, ReorderLevel, Unit, Status
                    FROM Products WHERE ProductID = @id AND IsActive = 1";
                var cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@id", id);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        product = new
                        {
                            ProductID = reader["ProductID"],
                            CategoryID = reader["CategoryID"],
                            SupplierID = reader["SupplierID"],
                            ProductName = reader["ProductName"],
                            Barcode = reader["Barcode"],
                            CostPrice = reader["CostPrice"],
                            SellingPrice = reader["SellingPrice"],
                            ReorderLevel = reader["ReorderLevel"],
                            Unit = reader["Unit"],
                            Status = reader["Status"]
                        };
                    }
                }
            }
            if (product == null) return NotFound();
            LoadCategoriesAndSuppliers();
            return View(product);
        }

        // ===============================
        // 5. EDIT PRODUCT (POST)
        // ===============================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, IFormCollection form)
        {
            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();
                string query = @"
                    UPDATE Products 
                    SET CategoryID = @cat, SupplierID = @sup, ProductName = @name, Barcode = @barcode,
                        CostPrice = @cost, SellingPrice = @sell, ReorderLevel = @reorder, Unit = @unit, Status = @status
                    WHERE ProductID = @id";
                var cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@cat", int.Parse(form["CategoryID"]));
                cmd.Parameters.AddWithValue("@sup", int.Parse(form["SupplierID"]));
                cmd.Parameters.AddWithValue("@name", form["ProductName"].ToString());
                cmd.Parameters.AddWithValue("@barcode", form["Barcode"].ToString());
                cmd.Parameters.AddWithValue("@cost", decimal.Parse(form["CostPrice"]));
                cmd.Parameters.AddWithValue("@sell", decimal.Parse(form["SellingPrice"]));
                cmd.Parameters.AddWithValue("@reorder", int.Parse(form["ReorderLevel"]));
                cmd.Parameters.AddWithValue("@unit", form["Unit"].ToString());
                cmd.Parameters.AddWithValue("@status", form["Status"].ToString());
                cmd.ExecuteNonQuery();
            }
            TempData["Success"] = "Product updated.";
            return RedirectToAction("Product");
        }

        // ===============================
        // 6. DELETE PRODUCT (Soft delete)
        // ===============================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();
                string query = "UPDATE Products SET IsActive = 0 WHERE ProductID = @id";
                var cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
            TempData["Success"] = "Product deleted (soft delete).";
            return RedirectToAction("Product");
        }

        // ===============================
        // 7. CATEGORIES MANAGEMENT
        // ===============================
        public IActionResult Categories(string search = "")
        {
            var categories = new List<dynamic>();
            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();
                string query = "SELECT CategoryID, CategoryName, Description, IsActive FROM Categories WHERE 1=1";
                if (!string.IsNullOrEmpty(search))
                {
                    query += " AND (CategoryName LIKE @search OR Description LIKE @search)";
                }
                var cmd = new SqlCommand(query, con);
                if (!string.IsNullOrEmpty(search))
                {
                    cmd.Parameters.AddWithValue("@search", "%" + search + "%");
                }
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        categories.Add(new
                        {
                            CategoryID = reader["CategoryID"],
                            CategoryName = reader["CategoryName"],
                            Description = reader["Description"],
                            IsActive = reader["IsActive"]
                        });
                    }
                }
            }
            ViewBag.Search = search;
            return View(categories);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddCategory(IFormCollection form)
        {
            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();
                string query = "INSERT INTO Categories (CategoryName, Description, IsActive) VALUES (@name, @desc, 1)";
                var cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@name", form["CategoryName"].ToString());
                cmd.Parameters.AddWithValue("@desc", form["Description"].ToString());
                cmd.ExecuteNonQuery();
            }
            TempData["Success"] = "Category added.";
            return RedirectToAction("Categories");
        }

        [HttpPost]
        public IActionResult DeleteCategory(int id)
        {
            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();
                // Check if category has products
                string checkQuery = "SELECT COUNT(*) FROM Products WHERE CategoryID = @id AND IsActive = 1";
                var checkCmd = new SqlCommand(checkQuery, con);
                checkCmd.Parameters.AddWithValue("@id", id);
                int count = (int)checkCmd.ExecuteScalar();
                if (count > 0)
                {
                    TempData["Error"] = "Cannot delete category with active products.";
                    return RedirectToAction("Categories");
                }
                string query = "DELETE FROM Categories WHERE CategoryID = @id";
                var cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                TempData["Success"] = "Category deleted.";
            }
            return RedirectToAction("Categories");
        }

        // ===============================
        // 8. STOCK ALERTS (Low stock)
        // ===============================
        public IActionResult StockAlerts()
        {
            var alerts = new List<dynamic>();
            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();
                string query = @"
                    SELECT p.ProductName, p.Barcode, i.QuantityInStock, p.ReorderLevel, s.SupplierName
                    FROM Products p
                    JOIN Inventory i ON p.ProductID = i.ProductID
                    LEFT JOIN Suppliers s ON p.SupplierID = s.SupplierID
                    WHERE p.IsActive = 1 AND i.QuantityInStock <= p.ReorderLevel
                    ORDER BY i.QuantityInStock ASC";
                var cmd = new SqlCommand(query, con);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        alerts.Add(new
                        {
                            ProductName = reader["ProductName"],
                            Barcode = reader["Barcode"],
                            QuantityInStock = reader["QuantityInStock"],
                            ReorderLevel = reader["ReorderLevel"],
                            SupplierName = reader["SupplierName"]
                        });
                    }
                }
            }
            return View(alerts);
        }

        // ===============================
        // Helper to load Categories & Suppliers for dropdowns
        // ===============================
        private void LoadCategoriesAndSuppliers()
        {
            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();

                // Categories (keep as is)
                var catCmd = new SqlCommand("SELECT CategoryID, CategoryName FROM Categories WHERE IsActive = 1", con);
                var cats = new List<dynamic>();
                using (var r = catCmd.ExecuteReader())
                    while (r.Read())
                        cats.Add(new { CategoryID = r["CategoryID"], CategoryName = r["CategoryName"] });
                ViewBag.Categories = cats;

                // Suppliers – ONLY ACTIVE ONES
                var supCmd = new SqlCommand("SELECT SupplierID, SupplierName FROM Suppliers WHERE IsActive = 1", con);
                var sups = new List<dynamic>();
                using (var r = supCmd.ExecuteReader())
                    while (r.Read())
                        sups.Add(new { SupplierID = r["SupplierID"], SupplierName = r["SupplierName"] });
                ViewBag.Suppliers = sups;
            }
        }
    }
}