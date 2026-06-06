using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using POSERP.Models.DataBase;
using POSERP.Models.Entities;
using POSERP.Models.ViewModels;
using POSERP.Services;
using System.Data;
using System.Security.Claims;

namespace POSERP.Controllers.Manager
{
    [Authorize(Roles = "Cashier,Manager,Admin")]
    public class POSController : Controller
    {
        private readonly Db _db;
        private readonly FraudDetectionService _fraudService;
        private readonly EmailService _emailService;
        public POSController(Db db, FraudDetectionService fraudService, EmailService emailService)
        {
            _db = db;
            _fraudService = fraudService;
            _emailService = emailService;
        }

        public IActionResult Index()
        {
            ViewBag.Products = GetProducts();
            ViewBag.Customers = GetCustomers();
            if (User.IsInRole("Cashier"))
            {
                return View("~/Views/Cashier/PointOfSale.cshtml");
            }
            return View("PointOfSale");
        }
        [HttpPost]
        public IActionResult AddQuickCustomer(string fullName, string phone, string address)
        {
            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(phone))
                return Json(new { success = false, message = "Name and phone are required" });

            using var conn = _db.GetConnection();
            conn.Open();
            // Check duplicate phone
            string checkQuery = "SELECT CustomerID FROM Customers WHERE Phone = @phone";
            using var checkCmd = new SqlCommand(checkQuery, conn);
            checkCmd.Parameters.AddWithValue("@phone", phone);
            var existing = checkCmd.ExecuteScalar();
            if (existing != null)
                return Json(new { success = true, customerId = Convert.ToInt32(existing), message = "Customer already exists" });

            string insertQuery = @"
        INSERT INTO Customers (FullName, Phone, Address, LoyaltyPoints, CreditBalance)
        VALUES (@name, @phone, @address, 0, 0);
        SELECT SCOPE_IDENTITY();";
            using var cmd = new SqlCommand(insertQuery, conn);
            cmd.Parameters.AddWithValue("@name", fullName);
            cmd.Parameters.AddWithValue("@phone", phone);
            cmd.Parameters.AddWithValue("@address", address ?? (object)DBNull.Value);
            int newId = Convert.ToInt32(cmd.ExecuteScalar());
            return Json(new { success = true, customerId = newId, message = "Customer added" });
        }

        [HttpPost]
        public IActionResult SearchProduct(string barcode, string productName)
        {
            var products = new List<ProductSearchViewModel>();
            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                SELECT p.ProductID, p.ProductName, p.Barcode, 
                       ISNULL(i.QuantityInStock, 0) as StockQuantity, p.SellingPrice
                FROM Products p
                LEFT JOIN Inventory i ON p.ProductID = i.ProductID
                WHERE p.IsActive = 1
                  AND (p.Barcode LIKE @Barcode OR p.ProductName LIKE @ProductName)
                ORDER BY p.ProductName";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Barcode", $"%{barcode}%");
            cmd.Parameters.AddWithValue("@ProductName", $"%{productName}%");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                products.Add(new ProductSearchViewModel
                {
                    ProductId = reader.GetInt32(reader.GetOrdinal("ProductID")),
                    ProductName = reader.GetString(reader.GetOrdinal("ProductName")),
                    Barcode = reader.GetString(reader.GetOrdinal("Barcode")),
                    StockQuantity = reader.GetInt32(reader.GetOrdinal("StockQuantity")),
                    SellingPrice = reader.GetDecimal(reader.GetOrdinal("SellingPrice"))
                });
            }
            return Json(products);
        }

        [HttpPost]
        public IActionResult GetProductById(int productId)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                SELECT p.ProductID, p.ProductName, p.Barcode, 
                       ISNULL(i.QuantityInStock, 0) as StockQuantity, p.SellingPrice
                FROM Products p
                LEFT JOIN Inventory i ON p.ProductID = i.ProductID
                WHERE p.ProductID = @ProductId AND p.IsActive = 1";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@ProductId", productId);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return Json(new ProductSearchViewModel
                {
                    ProductId = reader.GetInt32(reader.GetOrdinal("ProductID")),
                    ProductName = reader.GetString(reader.GetOrdinal("ProductName")),
                    Barcode = reader.GetString(reader.GetOrdinal("Barcode")),
                    StockQuantity = reader.GetInt32(reader.GetOrdinal("StockQuantity")),
                    SellingPrice = reader.GetDecimal(reader.GetOrdinal("SellingPrice"))
                });
            }
            return Json(null);
        }

        [HttpPost]
        public IActionResult CheckStock(int productId, int quantity)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string query = "SELECT QuantityInStock FROM Inventory WHERE ProductID = @pid";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@pid", productId);
            object result = cmd.ExecuteScalar();
            int stock = result != null ? Convert.ToInt32(result) : 0;
            return Json(new { available = stock >= quantity, stock });
        }

        [HttpPost]
        public IActionResult CompleteSale([FromBody] SaleViewModel sale)
        {
            if (sale == null || sale.CartItems == null || !sale.CartItems.Any())
                return Json(new { success = false, message = "Cart is empty" });

            using var conn = _db.GetConnection();
            conn.Open();
            using var transaction = conn.BeginTransaction();
            try
            {
                int cashierId = GetCurrentUserId(conn, transaction);
                string cashierName = User.Identity?.Name ?? "Cashier";

                // Calculate totals
                decimal subtotal = sale.CartItems.Sum(i => i.Quantity * i.Price);
                decimal discountAmount = subtotal * (sale.DiscountPercentage / 100);
                decimal taxableAmount = subtotal - discountAmount;
                decimal taxAmount = taxableAmount * (sale.TaxPercentage / 100);
                decimal totalAmount = taxableAmount + taxAmount;

                // Stock validation
                foreach (var item in sale.CartItems)
                {
                    string stockQuery = "SELECT QuantityInStock FROM Inventory WHERE ProductID = @pid";
                    using var cmdStock = new SqlCommand(stockQuery, conn, transaction);
                    cmdStock.Parameters.AddWithValue("@pid", item.ProductId);
                    object stockObj = cmdStock.ExecuteScalar();
                    int currentStock = stockObj != null ? Convert.ToInt32(stockObj) : 0;
                    if (currentStock < item.Quantity)
                    {
                        string productName = GetProductName(conn, transaction, item.ProductId);
                        throw new Exception($"Insufficient stock for {productName}. Available: {currentStock}");
                    }
                }

                // Insert Sale
                string insertSale = @"
                    INSERT INTO Sales (CustomerID, CashierID, SaleDate, TotalAmount, 
                                     DiscountAmount, TaxAmount, PaymentMethod, PaymentStatus)
                    VALUES (@CustomerId, @CashierId, GETDATE(), @TotalAmount, 
                           @DiscountAmount, @TaxAmount, @PaymentMethod, 'Paid');
                    SELECT SCOPE_IDENTITY();";
                int saleId;
                using var cmdSale = new SqlCommand(insertSale, conn, transaction);
                cmdSale.Parameters.AddWithValue("@CustomerId", sale.CustomerId > 0 ? sale.CustomerId : DBNull.Value);
                cmdSale.Parameters.AddWithValue("@CashierId", cashierId);
                cmdSale.Parameters.AddWithValue("@TotalAmount", totalAmount);
                cmdSale.Parameters.AddWithValue("@DiscountAmount", discountAmount);
                cmdSale.Parameters.AddWithValue("@TaxAmount", taxAmount);
                cmdSale.Parameters.AddWithValue("@PaymentMethod", sale.PaymentMethod);
                saleId = Convert.ToInt32(cmdSale.ExecuteScalar());

                // Insert SaleItems, update inventory, log history
                foreach (var item in sale.CartItems)
                {
                    string insertItem = @"
                        INSERT INTO SaleItems (SaleID, ProductID, Quantity, UnitPrice, Discount, SubTotal)
                        VALUES (@SaleId, @ProductId, @Quantity, @UnitPrice, 0, @SubTotal)";
                    using var cmdItem = new SqlCommand(insertItem, conn, transaction);
                    cmdItem.Parameters.AddWithValue("@SaleId", saleId);
                    cmdItem.Parameters.AddWithValue("@ProductId", item.ProductId);
                    cmdItem.Parameters.AddWithValue("@Quantity", item.Quantity);
                    cmdItem.Parameters.AddWithValue("@UnitPrice", item.Price);
                    cmdItem.Parameters.AddWithValue("@SubTotal", item.Quantity * item.Price);
                    cmdItem.ExecuteNonQuery();

                    // Get old stock
                    string getOldStock = "SELECT QuantityInStock FROM Inventory WHERE ProductID = @pid";
                    using var cmdOld = new SqlCommand(getOldStock, conn, transaction);
                    cmdOld.Parameters.AddWithValue("@pid", item.ProductId);
                    int oldStock = (int)cmdOld.ExecuteScalar();
                    int newStock = oldStock - item.Quantity;

                    // Update inventory
                    string updateInv = "UPDATE Inventory SET QuantityInStock = QuantityInStock - @qty, LastUpdated = GETDATE() WHERE ProductID = @pid";
                    using var cmdUpd = new SqlCommand(updateInv, conn, transaction);
                    cmdUpd.Parameters.AddWithValue("@qty", item.Quantity);
                    cmdUpd.Parameters.AddWithValue("@pid", item.ProductId);
                    cmdUpd.ExecuteNonQuery();

                    // Log to InventoryHistory
                    LogInventoryChange(conn, transaction, item.ProductId, oldStock, newStock, item.Quantity,
                        "Sale", $"Sale #{saleId}", $"Sold {item.Quantity} units", cashierName);
                }

                // Insert Payment
                string insertPayment = @"
                    INSERT INTO Payments (SaleID, PaymentDate, AmountPaid, PaymentMethod)
                    VALUES (@SaleId, GETDATE(), @Amount, @Method)";
                using var cmdPay = new SqlCommand(insertPayment, conn, transaction);
                cmdPay.Parameters.AddWithValue("@SaleId", saleId);
                cmdPay.Parameters.AddWithValue("@Amount", totalAmount);
                cmdPay.Parameters.AddWithValue("@Method", sale.PaymentMethod);
                cmdPay.ExecuteNonQuery();

                // Update customer loyalty points and credit
                if (sale.CustomerId > 0)
                {
                    int points = (int)(totalAmount / 10);
                    string updatePoints = "UPDATE Customers SET LoyaltyPoints = LoyaltyPoints + @points WHERE CustomerID = @cid";
                    using var cmdPoints = new SqlCommand(updatePoints, conn, transaction);
                    cmdPoints.Parameters.AddWithValue("@points", points);
                    cmdPoints.Parameters.AddWithValue("@cid", sale.CustomerId);
                    cmdPoints.ExecuteNonQuery();

                    if (sale.PaymentMethod == "Credit")
                    {
                        string updateCredit = "UPDATE Customers SET CreditBalance = CreditBalance + @amount WHERE CustomerID = @cid";
                        using var cmdCredit = new SqlCommand(updateCredit, conn, transaction);
                        cmdCredit.Parameters.AddWithValue("@amount", totalAmount);
                        cmdCredit.Parameters.AddWithValue("@cid", sale.CustomerId);
                        cmdCredit.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
                // After transaction.Commit();
                _ = _fraudService.CheckForFraud(saleId, "Sale", cashierId, sale.DiscountPercentage);

                // Get customer name
                string customerName = "Walk-in Customer";
                if (sale.CustomerId > 0)
                {
                    using var conn2 = _db.GetConnection();
                    conn2.Open();
                    string getCust = "SELECT FullName FROM Customers WHERE CustomerID = @cid";
                    using var cmdCust = new SqlCommand(getCust, conn2);
                    cmdCust.Parameters.AddWithValue("@cid", sale.CustomerId);
                    object nameObj = cmdCust.ExecuteScalar();
                    if (nameObj != null) customerName = nameObj.ToString();
                }

                string receiptNumber = $"INV-{DateTime.Now:yyyyMMdd}-{saleId}";
                var receipt = new ReceiptViewModel
                {
                    SaleId = saleId,
                    Date = DateTime.Now,
                    CustomerName = customerName,
                    CashierName = cashierName,
                    Items = sale.CartItems,
                    Subtotal = subtotal,
                    DiscountAmount = discountAmount,
                    DiscountPercent = sale.DiscountPercentage,
                    TaxAmount = taxAmount,
                    TaxPercent = sale.TaxPercentage,
                    TotalAmount = totalAmount,
                    PaymentMethod = sale.PaymentMethod,
                    ReceiptNumber = receiptNumber
                };

                string receiptHtml = RenderReceiptHtml(receipt);
                SaveReceiptToDb(conn, saleId, receiptNumber, receiptHtml);

                return Json(new { success = true, saleId, receipt });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult CancelSale([FromBody] CancelSaleModel model)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            using var transaction = conn.BeginTransaction();
            try
            {
                // First, get CashierID and check if already cancelled
                string checkQuery = "SELECT IsCancelled, CashierID FROM Sales WHERE SaleID = @id";
                using var cmdCheck = new SqlCommand(checkQuery, conn, transaction);
                cmdCheck.Parameters.AddWithValue("@id", model.SaleId);
                using var reader = cmdCheck.ExecuteReader();
                if (!reader.Read())
                {
                    transaction.Rollback();
                    return Json(new { success = false, message = "Sale not found" });
                }
                bool isCancelled = reader.GetBoolean(0);
                int cashierId = reader.GetInt32(1);
                reader.Close();

                if (isCancelled)
                {
                    transaction.Rollback();
                    return Json(new { success = false, message = "Sale already cancelled" });
                }

                // Get sale items
                string getItems = "SELECT ProductID, Quantity FROM SaleItems WHERE SaleID = @id";
                var items = new List<(int ProductId, int Quantity)>();
                using var cmdItems = new SqlCommand(getItems, conn, transaction);
                cmdItems.Parameters.AddWithValue("@id", model.SaleId);
                using var itemsReader = cmdItems.ExecuteReader();
                while (itemsReader.Read())
                {
                    items.Add((itemsReader.GetInt32(0), itemsReader.GetInt32(1)));
                }
                itemsReader.Close();

                // Restore inventory
                foreach (var (pid, qty) in items)
                {
                    string getStock = "SELECT QuantityInStock FROM Inventory WHERE ProductID = @pid";
                    using var cmdStock = new SqlCommand(getStock, conn, transaction);
                    cmdStock.Parameters.AddWithValue("@pid", pid);
                    int oldStock = (int)cmdStock.ExecuteScalar();
                    int newStock = oldStock + qty;

                    string updateInv = "UPDATE Inventory SET QuantityInStock = QuantityInStock + @qty, LastUpdated = GETDATE() WHERE ProductID = @pid";
                    using var cmdUpd = new SqlCommand(updateInv, conn, transaction);
                    cmdUpd.Parameters.AddWithValue("@qty", qty);
                    cmdUpd.Parameters.AddWithValue("@pid", pid);
                    cmdUpd.ExecuteNonQuery();

                    LogInventoryChange(conn, transaction, pid, oldStock, newStock, qty,
                        "Sale Cancellation", $"Sale #{model.SaleId}", $"Restored {qty} units", User.Identity?.Name ?? "System");
                }

                // Mark sale as cancelled
                string cancelQuery = @"
            UPDATE Sales SET IsCancelled = 1, CancellationReason = @reason, 
                            CancelledAt = GETDATE(), CancelledBy = @by
            WHERE SaleID = @id";
                using var cmdCancel = new SqlCommand(cancelQuery, conn, transaction);
                cmdCancel.Parameters.AddWithValue("@id", model.SaleId);
                cmdCancel.Parameters.AddWithValue("@reason", model.Reason ?? "No reason provided");
                cmdCancel.Parameters.AddWithValue("@by", User.Identity?.Name ?? "System");
                cmdCancel.ExecuteNonQuery();

                transaction.Commit();

                // Call fraud detection with the retrieved cashierId
                _ = _fraudService.CheckForFraud(model.SaleId, "Cancel", cashierId, 0);

                return Json(new { success = true, message = "Sale cancelled and stock restored" });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return Json(new { success = false, message = ex.Message });
            }
        }

        private string RenderReceiptHtml(ReceiptViewModel receipt)
        {
            // Build items table rows
            string itemsHtml = "";
            foreach (var item in receipt.Items)
            {
                itemsHtml += $"<tr><td>{item.ProductName}</td><td>{item.Quantity}</td><td>${item.Price:F2}</td><td>${(item.Quantity * item.Price):F2}</td></tr>";
            }

            string html = $@"
            <div style='font-family: monospace; width: 320px; margin: auto;'>
                <h3 style='text-align:center'>SmartPOS ERP</h3>
                <p style='text-align:center'>{receipt.Date:yyyy-MM-dd HH:mm:ss}<br/>Receipt: {receipt.ReceiptNumber}</p>
                <hr/>
                <p>Cashier: {receipt.CashierName}<br/>Customer: {receipt.CustomerName}</p>
                <hr/>
                <table style='width:100%'>
                    <tr><th>Item</th><th>Qty</th><th>Price</th><th>Total</th></tr>
                    {itemsHtml}
                </table>
                <hr/>
                <p>Subtotal: ${receipt.Subtotal:F2}<br/>
                Discount ({receipt.DiscountPercent}%): -${receipt.DiscountAmount:F2}<br/>
                Tax ({receipt.TaxPercent}%): ${receipt.TaxAmount:F2}<br/>
                <strong>Total: ${receipt.TotalAmount:F2}</strong><br/>
                Payment: {receipt.PaymentMethod}</p>
                <hr/>
                <p style='text-align:center'>Thank you for shopping!</p>
            </div>";
            return html;
        }

        private void SaveReceiptToDb(SqlConnection conn, int saleId, string receiptNumber, string receiptHtml)
        {
            string query = @"
                IF EXISTS (SELECT 1 FROM Receipts WHERE SaleID = @sid)
                    UPDATE Receipts SET ReceiptNumber = @num, ReceiptHTML = @html, GeneratedAt = GETDATE()
                    WHERE SaleID = @sid
                ELSE
                    INSERT INTO Receipts (SaleID, ReceiptNumber, ReceiptHTML, GeneratedAt)
                    VALUES (@sid, @num, @html, GETDATE())";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@sid", saleId);
            cmd.Parameters.AddWithValue("@num", receiptNumber);
            cmd.Parameters.AddWithValue("@html", receiptHtml);
            cmd.ExecuteNonQuery();
        }

        private int GetCurrentUserId(SqlConnection conn, SqlTransaction trans)
        {
            string email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.Identity?.Name;
            if (string.IsNullOrEmpty(email)) return 0;
            string query = "SELECT UserID FROM Users WHERE Email = @Email";
            using var cmd = new SqlCommand(query, conn, trans);
            cmd.Parameters.AddWithValue("@Email", email);
            var result = cmd.ExecuteScalar();
            if (result != null) return Convert.ToInt32(result);
            return 0;
        }

        private string GetProductName(SqlConnection conn, SqlTransaction trans, int productId)
        {
            string query = "SELECT ProductName FROM Products WHERE ProductID = @pid";
            using var cmd = new SqlCommand(query, conn, trans);
            cmd.Parameters.AddWithValue("@pid", productId);
            return cmd.ExecuteScalar()?.ToString() ?? "Unknown Product";
        }

        private void LogInventoryChange(SqlConnection conn, SqlTransaction trans,
            int productId, int oldStock, int newStock, int qtyChanged,
            string changeType, string reference, string remarks, string changedBy)
        {
            string sql = @"
                INSERT INTO InventoryHistory (ProductId, PreviousStock, NewStock, QuantityChanged,
                                              ChangeType, ReferenceNo, Remarks, ChangedAt, ChangedBy)
                VALUES (@pid, @prev, @new, @qty, @type, @ref, @rem, GETDATE(), @by)";
            using var cmd = new SqlCommand(sql, conn, trans);
            cmd.Parameters.AddWithValue("@pid", productId);
            cmd.Parameters.AddWithValue("@prev", oldStock);
            cmd.Parameters.AddWithValue("@new", newStock);
            cmd.Parameters.AddWithValue("@qty", qtyChanged);
            cmd.Parameters.AddWithValue("@type", changeType);
            cmd.Parameters.AddWithValue("@ref", reference);
            cmd.Parameters.AddWithValue("@rem", remarks);
            cmd.Parameters.AddWithValue("@by", changedBy);
            cmd.ExecuteNonQuery();
        }

        private List<dynamic> GetProducts()
        {
            var list = new List<dynamic>();
            using var conn = _db.GetConnection();
            conn.Open();
            string sql = "SELECT TOP 20 ProductID, ProductName, SellingPrice FROM Products WHERE IsActive = 1 ORDER BY ProductName";
            using var cmd = new SqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new
                {
                    Value = reader.GetInt32(0).ToString(),
                    Text = reader.GetString(1),
                    Price = reader.GetDecimal(2)
                });
            }
            return list;
        }

        private List<dynamic> GetCustomers()
        {
            var list = new List<dynamic>();
            using var conn = _db.GetConnection();
            conn.Open();
            string sql = "SELECT CustomerID, FullName, Phone, Address, LoyaltyPoints, CreditBalance FROM Customers ORDER BY FullName";
            using var cmd = new SqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new
                {
                    Value = reader.GetInt32(0).ToString(),
                    Text = reader.GetString(1),
                    Phone = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Address = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    LoyaltyPoints = reader.GetInt32(4),
                    CreditBalance = reader.GetDecimal(5)
                });
            }
            return list;
        }

        public class CancelSaleModel
        {
            public int SaleId { get; set; }
            public string Reason { get; set; } = string.Empty;
        }
        public IActionResult TestEmail()
        {
            _emailService.SendEmail("hananniazi0321@gmail.com", "Test", "If you see this, email works.");
            return Content("Check your inbox");
        }



    }
}