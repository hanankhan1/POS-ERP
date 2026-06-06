using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using POSERP.Models.DataBase;
using POSERP.Models.Expense;
using System;
using System.Collections.Generic;

namespace POSERP.Controllers.Manager
{
    [Authorize(Roles = "Manager,Admin")]
    public class ExpenseController : Controller
    {
        private readonly Db _db;

        public ExpenseController(Db db)
        {
            _db = db;
        }

        // ========== LIST ALL EXPENSES ==========
        public IActionResult Index(DateTime? fromDate, DateTime? toDate, string expenseType = "", string category = "")
        {
            DateTime from = fromDate ?? DateTime.Today.AddMonths(-1);
            DateTime to = toDate ?? DateTime.Today;
            ViewBag.FromDate = from.ToString("yyyy-MM-dd");
            ViewBag.ToDate = to.ToString("yyyy-MM-dd");
            ViewBag.SelectedType = expenseType;
            ViewBag.SelectedCategory = category;

            var expenses = new List<ExpenseViewModel>();
            using var conn = _db.GetConnection();
            conn.Open();

            string query = @"
                SELECT e.ExpenseID, e.ExpenseType, e.SourceID, e.Category, e.Amount, e.ExpenseDate, e.Description
                FROM Expenses e
                WHERE e.ExpenseDate BETWEEN @from AND @to
                  AND (@expenseType = '' OR e.ExpenseType = @expenseType)
                  AND (@category = '' OR e.Category = @category)
                ORDER BY e.ExpenseDate DESC";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@from", from);
            cmd.Parameters.AddWithValue("@to", to);
            cmd.Parameters.AddWithValue("@expenseType", expenseType);
            cmd.Parameters.AddWithValue("@category", category);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                expenses.Add(new ExpenseViewModel
                {
                    ExpenseID = reader.GetInt32(0),
                    ExpenseType = reader.GetString(1),
                    SourceID = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2),
                    Category = reader.GetString(3),
                    Amount = reader.GetDecimal(4),
                    ExpenseDate = reader.GetDateTime(5),
                    Description = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    SourceInfo = GetSourceInfo(reader.GetString(1), reader.IsDBNull(2) ? 0 : reader.GetInt32(2)).Replace("<a", "").Replace("</a>", "")
                });
            }
            return View(expenses);
        }

        // ========== ADD OPERATIONAL EXPENSE (GET) ==========
        public IActionResult Create()
        {
            return View();
        }

        // ========== ADD OPERATIONAL EXPENSE (POST) ==========
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(CreateExpenseViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            using var conn = _db.GetConnection();
            conn.Open();
            string sql = @"
                INSERT INTO Expenses (ExpenseType, Category, Amount, ExpenseDate, Description)
                VALUES ('Operational', @cat, @amt, @date, @desc)";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@cat", model.Category);
            cmd.Parameters.AddWithValue("@amt", model.Amount);
            cmd.Parameters.AddWithValue("@date", model.ExpenseDate);
            cmd.Parameters.AddWithValue("@desc", model.Description ?? (object)DBNull.Value);
            cmd.ExecuteNonQuery();

            TempData["Success"] = "Operational expense added successfully.";
            return RedirectToAction("Index");
        }

        // ========== DELETE EXPENSE (only manual operational ones) ==========
        [HttpPost]
        public IActionResult Delete(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            // Only allow deletion if it's an 'Operational' expense (auto-generated from other modules should not be deleted here)
            string check = "SELECT ExpenseType FROM Expenses WHERE ExpenseID = @id";
            using var cmdCheck = new SqlCommand(check, conn);
            cmdCheck.Parameters.AddWithValue("@id", id);
            string type = cmdCheck.ExecuteScalar()?.ToString();
            if (type != "Operational")
                return Json(new { success = false, message = "Only manual operational expenses can be deleted." });

            string sql = "DELETE FROM Expenses WHERE ExpenseID = @id";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            return Json(new { success = true });
        }

        // ========== HELPER: Build source info link/text ==========
        private string GetSourceInfo(string expenseType, int sourceId)
        {
            if (sourceId == 0) return "Manual Entry";
            switch (expenseType)
            {
                case "Purchase":
                    return $"<a href='/Purchase/Details/{sourceId}'>PO #{sourceId}</a>";
                case "Salary":
                    return $"<a href='/Employee/Payroll?highlight={sourceId}'>Payroll #{sourceId}</a>";
                case "EmployeeExpense":
                    return $"<a href='/Employee/Expenses?expenseId={sourceId}'>Expense #{sourceId}</a>";
                case "Discount":
                    return $"<a href='/POS/SaleDetails/{sourceId}'>Sale #{sourceId}</a>";
                case "InventoryLoss":
                    return $"<a href='/Inventory/InventoryHistory?id={sourceId}'>Adjustment #{sourceId}</a>";
                default:
                    return "";
            }
        }
    }
}