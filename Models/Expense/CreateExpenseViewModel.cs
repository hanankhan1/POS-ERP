using System.ComponentModel.DataAnnotations;

namespace POSERP.Models.Expense
{
    public class CreateExpenseViewModel
    {
        [Required]
        public string ExpenseType { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        [Required]
        [Range(0.01, double.MaxValue)]
        public decimal Amount { get; set; }

        [Required]
        public DateTime ExpenseDate { get; set; } = DateTime.Now;

        public string Description { get; set; } = string.Empty;
    }
}