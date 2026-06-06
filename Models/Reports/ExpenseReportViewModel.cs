namespace POSERP.Models.Reports
{
    public class ExpenseReportViewModel
    {
        public string ExpenseType { get; set; }
        public decimal TotalAmount { get; set; }
    }

    public class ExpenseSummaryViewModel
    {
        public decimal TotalSales { get; set; }
        public decimal TotalPayroll { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal NetProfit { get; set; }
        public decimal NetLoss { get; set; }
    }
}