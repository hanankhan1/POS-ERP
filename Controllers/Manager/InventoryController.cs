using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using POSERP.Models.DataBase;
using POSERP.Models.ViewModels;
using POSERP.Services;
using System;
using System.Collections.Generic;

namespace POSERP.Controllers.Manager
{
    [Authorize(Roles = "Manager,Admin")]
    public class InventoryController : Controller
    {
        private readonly Db _db;
        private readonly FraudDetectionService _fraudService;

        public InventoryController(Db db, FraudDetectionService fraudService)
        {
            _db = db;
            _fraudService = fraudService;
        }

        // ===============================
        // STOCK LEVELS (with search)
        // ===============================
        public IActionResult StockLevels(string search = "")
        {
            var stock = new List<dynamic>();
            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();
                string query = @"
                    SELECT p.ProductID, p.ProductName, p.Barcode, p.SellingPrice,
                           i.QuantityInStock, p.ReorderLevel, p.Unit, s.SupplierName,
                           CASE 
                               WHEN i.QuantityInStock <= 0 THEN 'Out of Stock'
                               WHEN i.QuantityInStock <= p.ReorderLevel THEN 'Low Stock'
                               ELSE 'In Stock'
                           END AS StockStatus
                    FROM Products p
                    JOIN Inventory i ON p.ProductID = i.ProductID
                    LEFT JOIN Suppliers s ON p.SupplierID = s.SupplierID
                    WHERE p.IsActive = 1";

                if (!string.IsNullOrEmpty(search))
                    query += " AND (p.ProductName LIKE @search OR p.Barcode LIKE @search OR s.SupplierName LIKE @search)";

                var cmd = new SqlCommand(query, con);
                if (!string.IsNullOrEmpty(search))
                    cmd.Parameters.AddWithValue("@search", "%" + search + "%");

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        stock.Add(new
                        {
                            ProductID = reader["ProductID"],
                            ProductName = reader["ProductName"],
                            Barcode = reader["Barcode"],
                            SellingPrice = reader["SellingPrice"],
                            QuantityInStock = reader["QuantityInStock"],
                            ReorderLevel = reader["ReorderLevel"],
                            Unit = reader["Unit"],
                            SupplierName = reader["SupplierName"],
                            StockStatus = reader["StockStatus"]
                        });
                    }
                }
            }
            ViewBag.Search = search;
            return View(stock);
        }

        // ===============================
        // ADJUST STOCK (GET)
        // ===============================
        public IActionResult AdjustStock(int id)
        {
            dynamic product = null;
            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();
                string query = @"
                    SELECT p.ProductID, p.ProductName, i.QuantityInStock, p.Unit
                    FROM Products p
                    JOIN Inventory i ON p.ProductID = i.ProductID
                    WHERE p.ProductID = @id AND p.IsActive = 1";
                var cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@id", id);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        product = new
                        {
                            ProductID = reader["ProductID"],
                            ProductName = reader["ProductName"],
                            CurrentStock = reader["QuantityInStock"],
                            Unit = reader["Unit"]
                        };
                    }
                }
            }
            if (product == null) return NotFound();
            return View(product);
        }

        // ===============================
        // ADJUST STOCK (POST)
        // ===============================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AdjustStock(int id, int newQuantity, string reason)
        {
            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();
                using (var transaction = con.BeginTransaction())
                {
                    try
                    {
                        // Get current stock
                        int oldStock;

                        using (var cmd = new SqlCommand(
                            "SELECT QuantityInStock FROM Inventory WHERE ProductID = @pid",
                            con,
                            transaction))
                        {
                            cmd.Parameters.AddWithValue("@pid", id);

                            var result = cmd.ExecuteScalar();
                            oldStock = result == null ? 0 : Convert.ToInt32(result);
                        }
                        ;

                        // Update inventory
                        var updateCmd = new SqlCommand("UPDATE Inventory SET QuantityInStock = @newQty, LastUpdated = GETDATE() WHERE ProductID = @pid", con, transaction);
                        updateCmd.Parameters.AddWithValue("@newQty", newQuantity);
                        updateCmd.Parameters.AddWithValue("@pid", id);
                        updateCmd.ExecuteNonQuery();

                        // Log to InventoryHistory
                        var logCmd = new SqlCommand(@"
                            INSERT INTO InventoryHistory (ProductId, PreviousStock, NewStock, QuantityChanged, ChangeType, Remarks, ChangedBy)
                            VALUES (@pid, @old, @new, @new - @old, 'ManualAdjust', @reason, @user)", con, transaction);
                        logCmd.Parameters.AddWithValue("@pid", id);
                        logCmd.Parameters.AddWithValue("@old", oldStock);
                        logCmd.Parameters.AddWithValue("@new", newQuantity);
                        logCmd.Parameters.AddWithValue("@reason", reason ?? "Manual adjustment");
                        logCmd.Parameters.AddWithValue("@user", User.Identity.Name);
                        logCmd.ExecuteNonQuery();

                        transaction.Commit();
                        _fraudService.CheckProductMismatch(id);
                        _fraudService.NotifyManualAdjustment(id, oldStock, newQuantity, reason, User.Identity.Name);
                        TempData["Success"] = $"Stock adjusted from {oldStock} to {newQuantity}.";
                    }
                    catch (Exception ex)
                    {
                        TempData["Error"] = "Adjustment failed: " + ex.Message;
                    }
                }
            }
            return RedirectToAction("StockLevels");
        }

        // ===============================
        // INVENTORY HISTORY (with search)
        // ===============================
        public IActionResult InventoryHistory(string search = "")
        {
            var logs = new List<dynamic>();
            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();
                string query = @"
                    SELECT ih.Id, p.ProductName, ih.PreviousStock, ih.NewStock, ih.QuantityChanged,
                           ih.ChangeType, ih.ReferenceNo, ih.Remarks, ih.ChangedAt, ih.ChangedBy
                    FROM InventoryHistory ih
                    JOIN Products p ON ih.ProductId = p.ProductID
                    WHERE 1=1";
                if (!string.IsNullOrEmpty(search))
                    query += " AND (p.ProductName LIKE @search OR ih.ChangeType LIKE @search OR ih.ChangedBy LIKE @search)";
                query += " ORDER BY ih.ChangedAt DESC";

                var cmd = new SqlCommand(query, con);
                if (!string.IsNullOrEmpty(search))
                    cmd.Parameters.AddWithValue("@search", "%" + search + "%");

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        logs.Add(new
                        {
                            Id = reader["Id"],
                            ProductName = reader["ProductName"],
                            PreviousStock = reader["PreviousStock"],
                            NewStock = reader["NewStock"],
                            QuantityChanged = reader["QuantityChanged"],
                            ChangeType = reader["ChangeType"],
                            ReferenceNo = reader["ReferenceNo"],
                            Remarks = reader["Remarks"],
                            ChangedAt = reader["ChangedAt"],
                            ChangedBy = reader["ChangedBy"]
                        });
                    }
                }
            }
            ViewBag.Search = search;
            return View(logs);
        }

        // ===============================
        // LOW STOCK REPORT
        // ===============================
        public IActionResult LowStock()
        {
            var lowStock = new List<dynamic>();
            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();
                string query = @"
                    SELECT p.ProductID, p.ProductName, p.Barcode, i.QuantityInStock, p.ReorderLevel,
                           p.Unit, s.SupplierName, s.Phone AS SupplierPhone
                    FROM Products p
                    JOIN Inventory i ON p.ProductID = i.ProductID
                    LEFT JOIN Suppliers s ON p.SupplierID = s.SupplierID
                    WHERE p.IsActive = 1 AND i.QuantityInStock <= p.ReorderLevel AND i.QuantityInStock > 0
                    ORDER BY i.QuantityInStock ASC";
                var cmd = new SqlCommand(query, con);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lowStock.Add(new
                        {
                            ProductID = reader["ProductID"],
                            ProductName = reader["ProductName"],
                            Barcode = reader["Barcode"],
                            QuantityInStock = reader["QuantityInStock"],
                            ReorderLevel = reader["ReorderLevel"],
                            Unit = reader["Unit"],
                            SupplierName = reader["SupplierName"],
                            SupplierPhone = reader["SupplierPhone"]
                        });
                    }
                }
            }
            return View(lowStock);
        }

        // ===============================
        // STOCK TRANSFERS
        // ===============================
        public IActionResult StockTransfers()
        {
            LoadTransferDropdowns();

            var transfers = new List<StockTransferViewModel>();
            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();
                string query = @"
                    SELECT st.TransferId, st.ProductId, p.ProductName, st.FromLocation, st.ToLocation,
                           st.Quantity, st.TransferDate, st.Status, st.Notes
                    FROM StockTransfers st
                    JOIN Products p ON st.ProductId = p.ProductID
                    ORDER BY st.TransferDate DESC";
                var cmd = new SqlCommand(query, con);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        transfers.Add(new StockTransferViewModel
                        {
                            TransferId = Convert.ToInt32(reader["TransferId"]),
                            ProductId = Convert.ToInt32(reader["ProductId"]),
                            ProductName = reader["ProductName"].ToString(),
                            FromLocation = reader["FromLocation"].ToString(),
                            ToLocation = reader["ToLocation"].ToString(),
                            Quantity = Convert.ToInt32(reader["Quantity"]),
                            TransferDate = Convert.ToDateTime(reader["TransferDate"]),
                            Status = reader["Status"].ToString(),
                            Notes = reader["Notes"]?.ToString()
                        });
                    }
                }
            }
            return View(transfers);
        }

        [HttpPost]
        public IActionResult CreateTransfer(int productId, string fromLocation, string toLocation, int quantity, string notes)
        {
            if (fromLocation == toLocation)
            {
                TempData["Error"] = "Source and destination locations cannot be the same.";
                return RedirectToAction("StockTransfers");
            }

            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();
                // Check stock availability
                var stockCmd = new SqlCommand("SELECT QuantityInStock FROM Inventory WHERE ProductID = @pid", con);
                stockCmd.Parameters.AddWithValue("@pid", productId);
                int currentStock = Convert.ToInt32(stockCmd.ExecuteScalar());
                if (currentStock < quantity)
                {
                    TempData["Error"] = $"Insufficient stock. Only {currentStock} available.";
                    return RedirectToAction("StockTransfers");
                }

                string insertQuery = @"
                    INSERT INTO StockTransfers (ProductId, FromLocation, ToLocation, Quantity, TransferDate, Status, Notes)
                    VALUES (@pid, @from, @to, @qty, GETDATE(), 'Pending', @notes)";
                var cmd = new SqlCommand(insertQuery, con);
                cmd.Parameters.AddWithValue("@pid", productId);
                cmd.Parameters.AddWithValue("@from", fromLocation);
                cmd.Parameters.AddWithValue("@to", toLocation);
                cmd.Parameters.AddWithValue("@qty", quantity);
                cmd.Parameters.AddWithValue("@notes", notes ?? "");
                cmd.ExecuteNonQuery();

                TempData["Success"] = "Stock transfer created. Pending approval.";
            }
            return RedirectToAction("StockTransfers");
        }

        [HttpPost]
        public IActionResult ApproveTransfer(int transferId)
        {
            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();
                using (var transaction = con.BeginTransaction())
                {
                    try
                    {
                        // Get transfer details
                        string getQuery = "SELECT ProductId, Quantity FROM StockTransfers WHERE TransferId = @tid AND Status = 'Pending'";
                        var getCmd = new SqlCommand(getQuery, con, transaction);
                        getCmd.Parameters.AddWithValue("@tid", transferId);
                        int productId = 0, quantity = 0;
                        using (var reader = getCmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                productId = Convert.ToInt32(reader["ProductId"]);
                                quantity = Convert.ToInt32(reader["Quantity"]);
                            }
                            else
                            {
                                TempData["Error"] = "Transfer not found or already processed.";
                                return RedirectToAction("StockTransfers");
                            }
                        }

                        // Deduct from inventory (simplified: no location-based stock)
                        var updateInv = new SqlCommand("UPDATE Inventory SET QuantityInStock = QuantityInStock - @qty WHERE ProductID = @pid", con, transaction);
                        updateInv.Parameters.AddWithValue("@qty", quantity);
                        updateInv.Parameters.AddWithValue("@pid", productId);
                        updateInv.ExecuteNonQuery();

                        // Log inventory history
                        var logCmd = new SqlCommand(@"
                            INSERT INTO InventoryHistory (ProductId, PreviousStock, NewStock, QuantityChanged, ChangeType, Remarks, ChangedBy)
                            SELECT @pid, QuantityInStock + @qty, QuantityInStock, -@qty, 'Transfer Out', @remarks, @user
                            FROM Inventory WHERE ProductID = @pid", con, transaction);
                        logCmd.Parameters.AddWithValue("@pid", productId);
                        logCmd.Parameters.AddWithValue("@qty", quantity);
                        logCmd.Parameters.AddWithValue("@remarks", $"Transfer approved #{transferId}");
                        logCmd.Parameters.AddWithValue("@user", User.Identity.Name);
                        logCmd.ExecuteNonQuery();

                        // Update transfer status
                        var statusCmd = new SqlCommand("UPDATE StockTransfers SET Status = 'Approved' WHERE TransferId = @tid", con, transaction);
                        statusCmd.Parameters.AddWithValue("@tid", transferId);
                        statusCmd.ExecuteNonQuery();

                        transaction.Commit();
                        TempData["Success"] = $"Transfer approved. Stock reduced by {quantity}.";
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        TempData["Error"] = "Approval failed: " + ex.Message;
                    }
                }
            }
            return RedirectToAction("StockTransfers");
        }

        private void LoadTransferDropdowns()
        {
            // Products dropdown
            var products = new List<SelectListItem>();
            using (SqlConnection con = _db.GetConnection())
            {
                con.Open();
                var cmd = new SqlCommand("SELECT ProductID, ProductName FROM Products WHERE IsActive = 1 ORDER BY ProductName", con);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        products.Add(new SelectListItem
                        {
                            Value = reader["ProductID"].ToString(),
                            Text = reader["ProductName"].ToString()
                        });
                    }
                }
            }
            ViewBag.Products = products;

            // Locations – you can replace with a Locations table later
            var locations = new List<SelectListItem>
            {
                new SelectListItem { Value = "Main Warehouse", Text = "Main Warehouse" },
                new SelectListItem { Value = "Store Front", Text = "Store Front" },
                new SelectListItem { Value = "Warehouse B", Text = "Warehouse B" }
            };
            ViewBag.Locations = locations;
        }
    }
}