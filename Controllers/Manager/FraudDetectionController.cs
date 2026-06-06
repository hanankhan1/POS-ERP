using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using POSERP.Models.DataBase;
using POSERP.Models.Fraud;
using POSERP.Services;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;

namespace POSERP.Controllers.Manager
{
    [Authorize(Roles = "Manager,Admin")]
    public class FraudDetectionController : Controller
    {
        private readonly Db _db;
        private readonly EmailService _emailService;

        public FraudDetectionController(Db db, EmailService emailService)
        {
            _db = db;
            _emailService = emailService;
        }

        // ========== LIST ALERTS ==========
        public IActionResult Index(string search = "", string riskLevel = "")
        {
            var alerts = new List<FraudAlertViewModel>();
            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
        SELECT fa.AlertID, fa.SaleID, fa.AlertType, fa.RiskLevel, fa.Description, 
               fa.CreatedAt, fa.Status, 
               ISNULL(u.FullName, 'System') AS CashierName, 
               ISNULL(s.TotalAmount, 0) AS TotalAmount
        FROM FraudAlerts fa
        LEFT JOIN Sales s ON fa.SaleID = s.SaleID
        LEFT JOIN Users u ON s.CashierID = u.UserID
        WHERE (@search = '' OR fa.AlertType LIKE @search OR fa.Description LIKE @search)
          AND (@riskLevel = '' OR fa.RiskLevel = @riskLevel)
        ORDER BY fa.CreatedAt DESC";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@search", $"%{search}%");
            cmd.Parameters.AddWithValue("@riskLevel", riskLevel);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                alerts.Add(new FraudAlertViewModel
                {
                    AlertID = reader.GetInt32(0),
                    SaleID = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                    AlertType = reader.GetString(2),
                    RiskLevel = reader.GetString(3),
                    Description = reader.GetString(4),
                    CreatedAt = reader.GetDateTime(5),
                    Status = reader.GetString(6),
                    CashierName = reader.GetString(7),
                    SaleAmount = reader.GetDecimal(8)
                });
            }
            ViewBag.Search = search;
            ViewBag.RiskLevel = riskLevel;
            return View(alerts);
        }

        // ========== ALERT DETAILS ==========
        public IActionResult Details(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string query = @"
        SELECT fa.AlertID, fa.SaleID, fa.AlertType, fa.RiskLevel, fa.Description, 
               fa.CreatedAt, fa.Status, 
               ISNULL(u.FullName, 'System') AS CashierName, 
               ISNULL(s.TotalAmount, 0) AS TotalAmount,
               ISNULL(s.DiscountAmount, 0) AS DiscountAmount,
               ISNULL(s.PaymentMethod, 'N/A') AS PaymentMethod,
               s.SaleDate
        FROM FraudAlerts fa
        LEFT JOIN Sales s ON fa.SaleID = s.SaleID
        LEFT JOIN Users u ON s.CashierID = u.UserID
        WHERE fa.AlertID = @id";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return NotFound();
            var alert = new FraudAlertViewModel
            {
                AlertID = reader.GetInt32(0),
                SaleID = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                AlertType = reader.GetString(2),
                RiskLevel = reader.GetString(3),
                Description = reader.GetString(4),
                CreatedAt = reader.GetDateTime(5),
                Status = reader.GetString(6),
                CashierName = reader.IsDBNull(7) ? "System" : reader.GetString(7),
                SaleAmount = reader.IsDBNull(8) ? 0 : reader.GetDecimal(8)
            };
            ViewBag.DiscountAmount = reader.GetDecimal(9);
            ViewBag.PaymentMethod = reader.GetString(10);
            ViewBag.SaleDate = reader.IsDBNull(11) ? (DateTime?)null : reader.GetDateTime(11);
            return View(alert);
        }

        // ========== DISMISS ALERT ==========
        [HttpPost]
        public IActionResult DismissAlert(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            string query = "UPDATE FraudAlerts SET Status = 'Dismissed' WHERE AlertID = @id";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            return RedirectToAction("Index");
        }

        // ========== SETTINGS ==========
        public IActionResult Settings()
        {
            var settings = new List<AlertSettingViewModel>();
            using var conn = _db.GetConnection();
            conn.Open();
            string query = "SELECT SettingID, SettingName, SettingValue, Description, NotifyEmails FROM AlertSettings";
            using var cmd = new SqlCommand(query, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                settings.Add(new AlertSettingViewModel
                {
                    SettingID = reader.GetInt32(0),
                    SettingName = reader.GetString(1),
                    SettingValue = reader.GetString(2),
                    Description = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    NotifyEmails = reader.IsDBNull(4) ? "" : reader.GetString(4)
                });
            }
            return View(settings);
        }

        [HttpPost]
        public IActionResult UpdateSettings(List<AlertSettingViewModel> settings)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            foreach (var s in settings)
            {
                string query = "UPDATE AlertSettings SET SettingValue = @val, NotifyEmails = @emails WHERE SettingID = @id";
                using var cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@val", s.SettingValue);
                cmd.Parameters.AddWithValue("@emails", s.NotifyEmails ?? "");
                cmd.Parameters.AddWithValue("@id", s.SettingID);
                cmd.ExecuteNonQuery();
            }
            TempData["Success"] = "Settings updated.";
            return RedirectToAction("Settings");
        }
        private void SendFraudAlertEmails(
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
                catch
                {

                }
            }
        }

        public IActionResult TestEmail()
        {
            _emailService.SendEmail("your-email@gmail.com", "Test", "If you see this, email works.");
            return Content("Check your inbox");
        }
    }
}