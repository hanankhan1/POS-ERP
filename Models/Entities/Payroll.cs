using System;

namespace POSERP.Models.Entities
{
    public class Payroll
    {
        public int PayrollID { get; set; }
        public int EmployeeID { get; set; }
        public string Month { get; set; }
        public decimal? BasicSalary { get; set; }
        public decimal Bonus { get; set; } = 0;
        public decimal Deductions { get; set; } = 0;
        public decimal? NetSalary { get; set; }
        public DateTime? PaymentDate { get; set; }

        public virtual Employee Employee { get; set; }
    }
}