using Microsoft.Data.SqlClient;
using POSERP.Models.DataBase;
using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using POSERP.Services;

namespace POSERP.Services
{
    public class FraudDetectionService
    {
        private readonly Db _db;
        private readonly EmailService _emailService;

        public FraudDetectionService(
            Db db,
            EmailService emailService)
        {
            _db = db;
            _emailService = emailService;
        }

        // Real-time check after sale or cancellation
        public async Task CheckForFraud(int saleId, string action, int cashierId, decimal discountPercent)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            // 1. Repeated cancellations by same cashier
            if (action == "Cancel")
            {
                int maxCancellations = GetAlertSetting(conn, "MaxCancellationsPerHour", 5);
                string cancelQuery = @"
                    SELECT COUNT(*) FROM Sales 
                    WHERE CashierID = @cid AND IsCancelled = 1 
                    AND CancelledAt >= DATEADD(hour, -1, GETDATE())";
                using var cmd = new SqlCommand(cancelQuery, conn);
                cmd.Parameters.AddWithValue("@cid", cashierId);
                int cancels = (int)cmd.ExecuteScalar();
                if (cancels > maxCancellations)
                {
                    await CreateAlert(conn, saleId, "Repeated Bill Cancellations", "High",
                        $"Cashier {cashierId} cancelled {cancels} bills in the last hour.");
                }
            }

            // 2. Unusual discount
            if (discountPercent > 0)
            {
                int maxDiscount = GetAlertSetting(conn, "MaxDiscountPercent", 20);
                if (discountPercent > maxDiscount)
                {
                    await CreateAlert(conn, saleId, "Unusual Discount", "Medium",
                        $"Discount {discountPercent}% exceeds allowed {maxDiscount}%.");
                }
            }

            // 3. Abnormal cashier activity (high sales per hour)
            int maxSalesPerHour = GetAlertSetting(conn, "MaxSalesPerHour", 30);
            string salesCountQuery = "SELECT COUNT(*) FROM Sales WHERE CashierID = @cid AND SaleDate >= DATEADD(hour, -1, GETDATE())";
            using var cmdCount = new SqlCommand(salesCountQuery, conn);
            cmdCount.Parameters.AddWithValue("@cid", cashierId);
            int salesCount = (int)cmdCount.ExecuteScalar();
            if (salesCount > maxSalesPerHour)
            {
                await CreateAlert(conn, saleId, "Abnormal Cashier Activity", "Medium",
                    $"Cashier made {salesCount} sales in the last hour (threshold: {maxSalesPerHour}).");
            }
        }

        // Nightly job: inventory mismatch check
        public void CheckInventoryMismatch()
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
                SELECT p.ProductID, p.ProductName, i.QuantityInStock, 
                       (SELECT ISNULL(SUM(poi.Quantity),0) FROM PurchaseOrderItems poi WHERE poi.ProductID = p.ProductID) - 
                       (SELECT ISNULL(SUM(si.Quantity),0) FROM SaleItems si JOIN Sales s ON si.SaleID = s.SaleID WHERE si.ProductID = p.ProductID AND s.IsCancelled = 0) AS ExpectedStock
                FROM Products p
                JOIN Inventory i ON p.ProductID = i.ProductID
                WHERE ABS(i.QuantityInStock - ((SELECT ISNULL(SUM(poi.Quantity),0) FROM PurchaseOrderItems poi WHERE poi.ProductID = p.ProductID) - 
                       (SELECT ISNULL(SUM(si.Quantity),0) FROM SaleItems si JOIN Sales s ON si.SaleID = s.SaleID WHERE si.ProductID = p.ProductID AND s.IsCancelled = 0))) > 5";
            using var cmd = new SqlCommand(query, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                int productId = reader.GetInt32(0);
                string productName = reader.GetString(1);
                int actual = reader.GetInt32(2);
                int expected = reader.GetInt32(3);
                CreateAlert(conn, null, "Inventory Mismatch", "High",
                    $"Product '{productName}' has actual stock {actual} but expected {expected}. Difference: {actual - expected}.");
            }
        }

        private int GetAlertSetting(SqlConnection conn, string settingName, int defaultValue)
        {
            string query = "SELECT SettingValue FROM AlertSettings WHERE SettingName = @name";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@name", settingName);
            var result = cmd.ExecuteScalar();
            if (result != null && int.TryParse(result.ToString(), out int val))
                return val;
            return defaultValue;
        }

        private async Task CreateAlert(SqlConnection conn, int? saleId, string alertType, string riskLevel, string description)
        {
            // Deduplication check (same alert within 5 minutes)
            string checkQuery = @"
        SELECT COUNT(*) FROM FraudAlerts 
        WHERE AlertType = @type AND Description = @desc AND CreatedAt >= DATEADD(minute, -5, GETDATE())";
            using var cmdCheck = new SqlCommand(checkQuery, conn);
            cmdCheck.Parameters.AddWithValue("@type", alertType);
            cmdCheck.Parameters.AddWithValue("@desc", description);
            int count = (int)cmdCheck.ExecuteScalar();
            if (count > 0) return;

            string insert = @"
        INSERT INTO FraudAlerts (SaleID, GeneratedByAI, AlertType, RiskLevel, Description, Status)
        VALUES (@sid, 1, @type, @risk, @desc, 'Pending')";
            using var cmd = new SqlCommand(insert, conn);
            cmd.Parameters.AddWithValue("@sid", saleId.HasValue ? (object)saleId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@type", alertType);
            cmd.Parameters.AddWithValue("@risk", riskLevel);
            cmd.Parameters.AddWithValue("@desc", description);
            await cmd.ExecuteNonQueryAsync();

            // Send email notification
            SendEmailNotification(alertType, riskLevel, description);
        }

        private void SendEmailNotification(
    string alertType,
    string riskLevel,
    string description)
        {
            using var conn = _db.GetConnection();

            conn.Open();

            string query =
                "SELECT NotifyEmails FROM AlertSettings " +
                "WHERE NotifyEmails IS NOT NULL " +
                "AND NotifyEmails <> ''";

            using var cmd = new SqlCommand(query, conn);

            using var reader = cmd.ExecuteReader();

            List<string> emails = new List<string>();

            while (reader.Read())
            {
                string emailString = reader["NotifyEmails"].ToString();

                if (!string.IsNullOrEmpty(emailString))
                {
                    string[] splitEmails =
                        emailString.Split(',');

                    foreach (string email in splitEmails)
                    {
                        string cleanEmail = email.Trim();

                        if (!emails.Contains(cleanEmail))
                        {
                            emails.Add(cleanEmail);
                        }
                    }
                }
            }

            foreach (string email in emails)
            {
                try
                {
                    string subject =
                        $"POS ERP Fraud Alert - {riskLevel} Risk";

                    string body =
                        $@"
                <h2>Fraud Alert Detected</h2>

                <p>
                    <strong>Alert Type:</strong>
                    {alertType}
                </p>

                <p>
                    <strong>Risk Level:</strong>
                    {riskLevel}
                </p>

                <p>
                    <strong>Description:</strong>
                    {description}
                </p>

                <p>
                    <strong>Generated At:</strong>
                    {DateTime.Now}
                </p>
                ";

                    _emailService.SendEmail(
                        email,
                        subject,
                        body
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }
    
    public void CheckProductMismatch(int productId)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            // Get actual stock
            string getActual = "SELECT QuantityInStock FROM Inventory WHERE ProductID = @pid";
            using var cmdActual = new SqlCommand(getActual, conn);
            cmdActual.Parameters.AddWithValue("@pid", productId);
            int actualStock = Convert.ToInt32(cmdActual.ExecuteScalar());

            // Get expected stock (purchased - sold)
            string getExpected = @"
        SELECT 
            ISNULL((SELECT SUM(Quantity) FROM PurchaseOrderItems WHERE ProductID = @pid), 0) -
            ISNULL((SELECT SUM(si.Quantity) 
                    FROM SaleItems si 
                    JOIN Sales s ON si.SaleID = s.SaleID 
                    WHERE si.ProductID = @pid AND s.IsCancelled = 0), 0) AS ExpectedStock";
            using var cmdExpected = new SqlCommand(getExpected, conn);
            cmdExpected.Parameters.AddWithValue("@pid", productId);
            int expectedStock = Convert.ToInt32(cmdExpected.ExecuteScalar());

            int difference = actualStock - expectedStock;
            int threshold = GetAlertSetting(conn, "InventoryMismatchThreshold", 5);

            if (Math.Abs(difference) > threshold)
            {
                string productName = GetProductName(conn, productId);
                string description =
     $"ALERT: Manual stock adjustment detected for Product '{productName}' (ID: {productId}). " +
     $"Actual stock = {actualStock}, Expected stock = {expectedStock}, Difference = {difference}. " +
     $"No purchase order or sales transaction matches this change. This may indicate unauthorized or manual inventory modification.";
                // Create alert (saleId = null because no specific sale)
                CreateAlert(conn, null, "Inventory Mismatch", "High", description).Wait();
            }
        }

        // Helper to get product name
        private string GetProductName(SqlConnection conn, int productId)
        {
            string query = "SELECT ProductName FROM Products WHERE ProductID = @pid";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@pid", productId);
            return cmd.ExecuteScalar()?.ToString() ?? "Unknown Product";
        }

        public void NotifyManualAdjustment(int productId, int oldStock, int newStock, string reason, string changedBy)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            // Get product name
            string productName = GetProductName(conn, productId);

            // Get all notification emails from AlertSettings
            string emails = GetNotificationEmails(conn);
            if (string.IsNullOrEmpty(emails)) return;

            string subject = $"Manual Stock Adjustment - {productName}";
            string body = $@"
        <h3>Manual Stock Adjustment Performed</h3>
        <p><strong>Product:</strong> {productName} (ID: {productId})</p>
        <p><strong>Adjusted By:</strong> {changedBy}</p>
        <p><strong>Old Stock:</strong> {oldStock}</p>
        <p><strong>New Stock:</strong> {newStock}</p>
        <p><strong>Change:</strong> {(newStock - oldStock):+#;-#;0}</p>
        <p><strong>Reason:</strong> {reason ?? "Not specified"}</p>
        <p><strong>Date/Time:</strong> {DateTime.Now}</p>
        <hr/>
        <p>This email is sent for every manual inventory adjustment.</p>";

            foreach (string email in emails.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                _emailService.SendEmail(email.Trim(), subject, body);
            }
        }

        private string GetNotificationEmails(SqlConnection conn)
        {
            var emails = new List<string>();
            string query = "SELECT NotifyEmails FROM AlertSettings WHERE NotifyEmails IS NOT NULL AND NotifyEmails <> ''";
            using var cmd = new SqlCommand(query, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string emailString = reader.GetString(0);
                if (!string.IsNullOrEmpty(emailString))
                {
                    foreach (var e in emailString.Split(','))
                    {
                        string clean = e.Trim();
                        if (!emails.Contains(clean)) emails.Add(clean);
                    }
                }
            }
            return string.Join(",", emails);
        }


    } 
}