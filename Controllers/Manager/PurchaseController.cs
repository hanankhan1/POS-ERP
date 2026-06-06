using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using POSERP.Models.DataBase;
using POSERP.Models.Purchase;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace POSERP.Controllers.Manager
{
    [Authorize(Roles = "Manager,Admin")]
    public class PurchaseController : Controller
    {
        private readonly Db _db;
        public PurchaseController(Db db) => _db = db;

        // ========== LIST PURCHASE ORDERS ==========
        public IActionResult Index()
        {
            var orders = new List<PurchaseOrderViewModel>();
            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                SELECT po.PurchaseOrderID, po.SupplierID, s.SupplierName, po.OrderDate, po.TotalAmount, po.Status
                FROM PurchaseOrders po
                JOIN Suppliers s ON po.SupplierID = s.SupplierID
                ORDER BY po.OrderDate DESC";
            using var cmd = new SqlCommand(query, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                orders.Add(new PurchaseOrderViewModel
                {
                    PurchaseOrderID = reader.GetInt32(0),
                    SupplierID = reader.GetInt32(1),
                    SupplierName = reader.GetString(2),
                    OrderDate = reader.GetDateTime(3),
                    TotalAmount = reader.GetDecimal(4),
                    Status = reader.GetString(5)
                });
            }
            return View(orders);
        }

        // ========== CREATE PURCHASE ORDER (GET) ==========
        [HttpGet]
        public IActionResult Create()
        {
            LoadSuppliers();
            LoadProducts();
            return View();
        }

        // ========== CREATE PURCHASE ORDER (POST) ==========
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(CreatePurchaseOrderViewModel model)
        {
            // Validate supplier
            if (model.SupplierID <= 0)
                ModelState.AddModelError("SupplierID", "Please select a supplier.");

            // Validate items
            if (model.Items == null || !model.Items.Any() || model.Items.All(i => i.ProductID == 0))
            {
                ModelState.AddModelError("", "Please add at least one product.");
            }
            else
            {
                // Remove any empty items
                model.Items = model.Items.Where(i => i.ProductID > 0 && i.Quantity > 0).ToList();
                if (!model.Items.Any())
                    ModelState.AddModelError("", "Please add valid products with quantity.");
            }

            // Optional: check for duplicate products
            if (model.Items != null && model.Items.Any())
            {
                var duplicates = model.Items.GroupBy(i => i.ProductID).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                if (duplicates.Any())
                    ModelState.AddModelError("", "Duplicate products are not allowed in the same purchase order.");
            }

            if (!ModelState.IsValid)
            {
                LoadSuppliers();
                LoadProducts();
                return View(model);
            }

            using var conn = _db.GetConnection();
            conn.Open();
            using var transaction = conn.BeginTransaction();
            try
            {
                int userId = GetCurrentUserId(conn, transaction);
                if (userId == 0)
                    throw new Exception("No valid user found. Please log in again or contact admin.");

                decimal totalAmount = model.Items?
    .Where(i => i.ProductID > 0 && i.Quantity > 0)
    .Sum(i => i.Quantity * i.CostPrice) ?? 0;

                // Insert Purchase Order
                string insertPO = @"
                    INSERT INTO PurchaseOrders (SupplierID, CreatedBy, OrderDate, TotalAmount, Status)
                    VALUES (@SupplierID, @CreatedBy, GETDATE(), @Total, 'Pending');
                    SELECT SCOPE_IDENTITY();";
                int poId;
                using var cmdPO = new SqlCommand(insertPO, conn, transaction);
                cmdPO.Parameters.AddWithValue("@SupplierID", model.SupplierID);
                cmdPO.Parameters.AddWithValue("@CreatedBy", userId);
                cmdPO.Parameters.AddWithValue("@Total", totalAmount);
                poId = Convert.ToInt32(cmdPO.ExecuteScalar());

                // Insert Items
                foreach (var item in model.Items)
                {
                    string insertItem = @"
                        INSERT INTO PurchaseOrderItems (PurchaseOrderID, ProductID, Quantity, CostPrice, SubTotal)
                        VALUES (@POID, @ProductID, @Qty, @Price, @Sub)";
                    using var cmdItem = new SqlCommand(insertItem, conn, transaction);
                    cmdItem.Parameters.AddWithValue("@POID", poId);
                    cmdItem.Parameters.AddWithValue("@ProductID", item.ProductID);
                    cmdItem.Parameters.AddWithValue("@Qty", item.Quantity);
                    cmdItem.Parameters.AddWithValue("@Price", item.CostPrice);
                    cmdItem.Parameters.AddWithValue("@Sub", item.Quantity * item.CostPrice);
                    cmdItem.ExecuteNonQuery();
                }

                transaction.Commit();
                TempData["Success"] = "Purchase order created successfully.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                TempData["Error"] = "Error creating purchase order: " + ex.Message;
                LoadSuppliers();
                LoadProducts();
                return View(model);
            }
        }

        // ========== EDIT PURCHASE ORDER (GET) ==========
        [HttpGet]
        public IActionResult Edit(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                SELECT po.PurchaseOrderID, po.SupplierID, s.SupplierName, po.OrderDate, po.TotalAmount, po.Status
                FROM PurchaseOrders po
                JOIN Suppliers s ON po.SupplierID = s.SupplierID
                WHERE po.PurchaseOrderID = @id";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return NotFound();
            var order = new PurchaseOrderViewModel
            {
                PurchaseOrderID = reader.GetInt32(0),
                SupplierID = reader.GetInt32(1),
                SupplierName = reader.GetString(2),
                OrderDate = reader.GetDateTime(3),
                TotalAmount = reader.GetDecimal(4),
                Status = reader.GetString(5)
            };
            reader.Close();

            // Get items
            string itemQuery = @"
                SELECT poi.ProductID, p.ProductName, p.Barcode, poi.Quantity, poi.CostPrice, poi.SubTotal
                FROM PurchaseOrderItems poi
                JOIN Products p ON poi.ProductID = p.ProductID
                WHERE poi.PurchaseOrderID = @id";
            var items = new List<PurchaseOrderItemViewModel>();
            using var cmdItems = new SqlCommand(itemQuery, conn);
            cmdItems.Parameters.AddWithValue("@id", id);
            using var itemsReader = cmdItems.ExecuteReader();
            while (itemsReader.Read())
            {
                items.Add(new PurchaseOrderItemViewModel
                {
                    ProductID = itemsReader.GetInt32(0),
                    ProductName = itemsReader.GetString(1),
                    Barcode = itemsReader.GetString(2),
                    Quantity = itemsReader.GetInt32(3),
                    CostPrice = itemsReader.GetDecimal(4),
                    SubTotal = itemsReader.GetDecimal(5)
                });
            }
            order.Items = items;

            LoadSuppliers();
            LoadProducts();
            return View(order);
        }

        // ========== EDIT PURCHASE ORDER (POST) ==========
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(PurchaseOrderViewModel model)
        {
            if (model.Status != "Pending")
            {
                TempData["Error"] = "Cannot edit a completed order.";
                return RedirectToAction("Details", new { id = model.PurchaseOrderID });
            }

            if (!ModelState.IsValid || model.Items == null || !model.Items.Any())
            {
                LoadSuppliers();
                LoadProducts();
                return View(model);
            }

            using var conn = _db.GetConnection();
            conn.Open();
            using var transaction = conn.BeginTransaction();
            try
            {
                decimal totalAmount = model.Items.Sum(i => i.Quantity * i.CostPrice);

                // Update purchase order
                string updatePO = @"
                    UPDATE PurchaseOrders 
                    SET SupplierID = @SupplierID, TotalAmount = @Total
                    WHERE PurchaseOrderID = @id AND Status = 'Pending'";
                using var cmdPO = new SqlCommand(updatePO, conn, transaction);
                cmdPO.Parameters.AddWithValue("@SupplierID", model.SupplierID);
                cmdPO.Parameters.AddWithValue("@Total", totalAmount);
                cmdPO.Parameters.AddWithValue("@id", model.PurchaseOrderID);
                cmdPO.ExecuteNonQuery();

                // Delete old items
                string delItems = "DELETE FROM PurchaseOrderItems WHERE PurchaseOrderID = @id";
                using var cmdDel = new SqlCommand(delItems, conn, transaction);
                cmdDel.Parameters.AddWithValue("@id", model.PurchaseOrderID);
                cmdDel.ExecuteNonQuery();

                // Insert new items
                foreach (var item in model.Items)
                {
                    string insertItem = @"
                        INSERT INTO PurchaseOrderItems (PurchaseOrderID, ProductID, Quantity, CostPrice, SubTotal)
                        VALUES (@POID, @ProductID, @Qty, @Price, @Sub)";
                    using var cmdItem = new SqlCommand(insertItem, conn, transaction);
                    cmdItem.Parameters.AddWithValue("@POID", model.PurchaseOrderID);
                    cmdItem.Parameters.AddWithValue("@ProductID", item.ProductID);
                    cmdItem.Parameters.AddWithValue("@Qty", item.Quantity);
                    cmdItem.Parameters.AddWithValue("@Price", item.CostPrice);
                    cmdItem.Parameters.AddWithValue("@Sub", item.Quantity * item.CostPrice);
                    cmdItem.ExecuteNonQuery();
                }

                transaction.Commit();
                TempData["Success"] = "Purchase order updated.";
                return RedirectToAction("Details", new { id = model.PurchaseOrderID });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                TempData["Error"] = "Error updating order: " + ex.Message;
                LoadSuppliers();
                LoadProducts();
                return View(model);
            }
        }

        // ========== DETAILS ==========
        public IActionResult Details(int id)
        {
            var order = new PurchaseOrderViewModel();
            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                SELECT po.PurchaseOrderID, po.SupplierID, s.SupplierName, po.OrderDate, po.TotalAmount, po.Status
                FROM PurchaseOrders po
                JOIN Suppliers s ON po.SupplierID = s.SupplierID
                WHERE po.PurchaseOrderID = @id";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return NotFound();
            order.PurchaseOrderID = reader.GetInt32(0);
            order.SupplierID = reader.GetInt32(1);
            order.SupplierName = reader.GetString(2);
            order.OrderDate = reader.GetDateTime(3);
            order.TotalAmount = reader.GetDecimal(4);
            order.Status = reader.GetString(5);
            reader.Close();

            // Get items
            string itemQuery = @"
                SELECT poi.ProductID, p.ProductName, p.Barcode, poi.Quantity, poi.CostPrice, poi.SubTotal
                FROM PurchaseOrderItems poi
                JOIN Products p ON poi.ProductID = p.ProductID
                WHERE poi.PurchaseOrderID = @id";
            using var cmdItems = new SqlCommand(itemQuery, conn);
            cmdItems.Parameters.AddWithValue("@id", id);
            using var itemsReader = cmdItems.ExecuteReader();
            var items = new List<PurchaseOrderItemViewModel>();
            while (itemsReader.Read())
            {
                items.Add(new PurchaseOrderItemViewModel
                {
                    ProductID = itemsReader.GetInt32(0),
                    ProductName = itemsReader.GetString(1),
                    Barcode = itemsReader.GetString(2),
                    Quantity = itemsReader.GetInt32(3),
                    CostPrice = itemsReader.GetDecimal(4),
                    SubTotal = itemsReader.GetDecimal(5)
                });
            }
            order.Items = items;
            return View(order);
        }

        // ========== RECEIVE PURCHASE ORDER (Update Inventory) ==========
        [HttpPost]
        public IActionResult ReceiveOrder(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            using var transaction = conn.BeginTransaction();

            try
            {
                // check status
                string checkStatus = "SELECT Status FROM PurchaseOrders WHERE PurchaseOrderID = @id";
                using var cmdCheck = new SqlCommand(checkStatus, conn, transaction);
                cmdCheck.Parameters.AddWithValue("@id", id);

                string status = cmdCheck.ExecuteScalar()?.ToString() ?? "";

                if (status == "Received")
                    return Json(new { success = false, message = "Order already received." });

                // get items
                string itemQuery = "SELECT ProductID, Quantity FROM PurchaseOrderItems WHERE PurchaseOrderID = @id";
                var items = new List<(int ProductId, int Quantity)>();

                using (var cmdItems = new SqlCommand(itemQuery, conn, transaction))
                {
                    cmdItems.Parameters.AddWithValue("@id", id);
                    using var reader = cmdItems.ExecuteReader();

                    while (reader.Read())
                        items.Add((reader.GetInt32(0), reader.GetInt32(1)));
                }

                string userName = User.Identity?.Name ?? "System";

                foreach (var (pid, qty) in items)
                {
                    // SAFE STOCK CHECK
                    string getStock = "SELECT ISNULL(QuantityInStock,0) FROM Inventory WHERE ProductID = @pid";
                    using var cmdStock = new SqlCommand(getStock, conn, transaction);
                    cmdStock.Parameters.AddWithValue("@pid", pid);

                    int oldStock = Convert.ToInt32(cmdStock.ExecuteScalar());
                    int newStock = oldStock + qty;

                    // update or insert inventory safety
                    string upsert = @"
IF EXISTS (SELECT 1 FROM Inventory WHERE ProductID=@pid)
    UPDATE Inventory 
    SET QuantityInStock=@qty, LastUpdated=GETDATE()
    WHERE ProductID=@pid
ELSE
    INSERT INTO Inventory(ProductID, QuantityInStock, LastUpdated)
    VALUES(@pid, @qty, GETDATE())";

                    using var cmdUpsert = new SqlCommand(upsert, conn, transaction);
                    cmdUpsert.Parameters.AddWithValue("@pid", pid);
                    cmdUpsert.Parameters.AddWithValue("@qty", newStock);
                    cmdUpsert.ExecuteNonQuery();

                    // history log
                    string logQuery = @"
INSERT INTO InventoryHistory
(ProductId, PreviousStock, NewStock, QuantityChanged, ChangeType, ReferenceNo, Remarks, ChangedBy)
VALUES
(@pid, @old, @new, @qty, 'Purchase', @ref, 'PO Received', @user)";

                    using var cmdLog = new SqlCommand(logQuery, conn, transaction);
                    cmdLog.Parameters.AddWithValue("@pid", pid);
                    cmdLog.Parameters.AddWithValue("@old", oldStock);
                    cmdLog.Parameters.AddWithValue("@new", newStock);
                    cmdLog.Parameters.AddWithValue("@qty", qty);
                    cmdLog.Parameters.AddWithValue("@ref", $"PO#{id}");
                    cmdLog.Parameters.AddWithValue("@user", userName);
                    cmdLog.ExecuteNonQuery();
                }

                // update status
                string updatePO = "UPDATE PurchaseOrders SET Status = 'Received' WHERE PurchaseOrderID = @id";
                using var cmdPO = new SqlCommand(updatePO, conn, transaction);
                cmdPO.Parameters.AddWithValue("@id", id);
                cmdPO.ExecuteNonQuery();

                transaction.Commit();

                return RedirectToAction("Details", new { id });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return Json(new { success = false, message = ex.Message });
            }
        }
        // ========== HELPER: Load active suppliers ==========
        private void LoadSuppliers()
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string query = "SELECT SupplierID, SupplierName FROM Suppliers WHERE IsActive = 1 ORDER BY SupplierName";
            var suppliers = new List<SelectListItem>();
            using var cmd = new SqlCommand(query, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                suppliers.Add(new SelectListItem
                {
                    Value = reader.GetInt32(0).ToString(),
                    Text = reader.GetString(1)
                });
            }
            ViewBag.Suppliers = suppliers;
        }

        // ========== HELPER: Load active products ==========
        private void LoadProducts()
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string query = "SELECT ProductID, ProductName, SellingPrice FROM Products WHERE IsActive = 1 ORDER BY ProductName";
            var products = new List<dynamic>();
            using var cmd = new SqlCommand(query, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                products.Add(new
                {
                    ProductID = reader.GetInt32(0),
                    ProductName = reader.GetString(1),
                    SellingPrice = reader.GetDecimal(2)
                });
            }
            ViewBag.Products = products;
        }

        // ========== HELPER: Get current UserID (reliable) ==========
        private int GetCurrentUserId(SqlConnection conn = null, SqlTransaction trans = null)
        {
            // Try claim first
            var claim = User.FindFirst("UserID");
            if (claim != null && int.TryParse(claim.Value, out int userId))
                return userId;

            // Fallback: query by email
            string email = User.Identity?.Name;
            if (string.IsNullOrEmpty(email)) return 0;

            bool needClose = false;
            if (conn == null)
            {
                conn = _db.GetConnection();
                conn.Open();
                needClose = true;
            }
            try
            {
                string query = "SELECT UserID FROM Users WHERE Email = @email";
                using var cmd = new SqlCommand(query, conn, trans);
                cmd.Parameters.AddWithValue("@email", email);
                var result = cmd.ExecuteScalar();
                if (result != null)
                    return Convert.ToInt32(result);
                return 0;
            }
            finally
            {
                if (needClose) conn.Close();
            }
        }
    }
}