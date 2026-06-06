using System;

namespace POSERP.Models.Expense
{
    public class ExpenseViewModel
    {
        public int ExpenseID { get; set; }
        public string ExpenseType { get; set; }      // Purchase, Salary, EmployeeExpense, Discount, InventoryLoss, Operational
        public int? SourceID { get; set; }            // PurchaseOrderID, PayrollID, EmployeeExpenseID, SaleID, InventoryHistoryID
        public string Category { get; set; }          // e.g., COGS, Travel, Utilities, etc.
        public decimal Amount { get; set; }
        public DateTime ExpenseDate { get; set; }
        public string Description { get; set; }
        public string SourceInfo { get; set; }        // Display link or reference (not stored)
    }
}