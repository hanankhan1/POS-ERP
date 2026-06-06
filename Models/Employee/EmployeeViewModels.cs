
    using System.ComponentModel.DataAnnotations;

namespace POSERP.Models.Employee
{
    public class EmployeeViewModel
    {
        public int EmployeeID { get; set; }

        public int UserID { get; set; }

        [Required(ErrorMessage = "Full name is required")]
        [StringLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Role is required")]
        public string Role { get; set; } = string.Empty;

        [Required(ErrorMessage = "CNIC is required")]
        [StringLength(20)]
        public string CNIC { get; set; } = string.Empty;

        [Required(ErrorMessage = "Phone is required")]
        [Phone]
        public string Phone { get; set; } = string.Empty;

        public DateTime? HireDate { get; set; }

        [Range(0, 999999999)]
        public decimal Salary { get; set; }

        public string Designation { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        // CALCULATED VALUES
        public int TotalPresent { get; set; }

        public int TotalAbsent { get; set; }

        public int TotalLate { get; set; }

        public decimal TotalExpenses { get; set; }
    }


    // =========================================================
    // ATTENDANCE
    // =========================================================
    public class AttendanceViewModel
    {
        public int AttendanceID { get; set; }

        public int EmployeeID { get; set; }

        public string EmployeeName { get; set; } = string.Empty;

        public DateTime AttendanceDate { get; set; }

        public TimeSpan? CheckInTime { get; set; }

        public TimeSpan? CheckOutTime { get; set; }

        public decimal WorkingHours { get; set; }

        public decimal OvertimeHours { get; set; }

        public string Status { get; set; } = string.Empty;
    }

    // =========================================================
    // PAYROLL
    // =========================================================
    public class PayrollViewModel
    {
        public int PayrollID { get; set; }

        public int EmployeeID { get; set; }

        public string EmployeeName { get; set; } = string.Empty;

        public string Month { get; set; } = string.Empty;

        public decimal BasicSalary { get; set; }

        public decimal Bonus { get; set; }

        public decimal Deductions { get; set; }

        public decimal OvertimeAmount { get; set; }

        public decimal NetSalary { get; set; }

        public DateTime? PaymentDate { get; set; }
    }

    // =========================================================
    // PERFORMANCE
    // =========================================================
    public class PerformanceViewModel
    {
        public int PerformanceID { get; set; }

        public int EmployeeID { get; set; }

        public string EmployeeName { get; set; } = string.Empty;

        public DateTime ReviewDate { get; set; }

        public decimal? SalesTarget { get; set; }

        public decimal? SalesAchieved { get; set; }

        public int? CustomerRating { get; set; }

        public string Comments { get; set; } = string.Empty;

        // ================= CALCULATED =================
        public decimal AchievementPercentage { get; set; }
    }

    // =========================================================
    // ATTENDANCE POST MODEL
    // =========================================================
    public class AttendancePostModel
    {
        public int EmployeeId { get; set; }

        public string CheckIn { get; set; } = "";

        public string CheckOut { get; set; } = "";

        public string Status { get; set; } = "Absent";
    }

    // =========================================================
    // MANAGE ATTENDANCE
    // =========================================================
    public class ManageAttendanceViewModel
    {
        public int EmployeeID { get; set; }

        public string FullName { get; set; } = string.Empty;

        public int AttendanceID { get; set; }

        public TimeSpan? CheckInTime { get; set; }

        public TimeSpan? CheckOutTime { get; set; }

        public decimal WorkingHours { get; set; }

        public decimal OvertimeHours { get; set; }

        public string Status { get; set; } = string.Empty;
    }

    // =========================================================
    // EMPLOYEE EXPENSES
    // =========================================================
    public class EmployeeExpenseViewModel
    {
        public int ExpenseID { get; set; }

        public int EmployeeID { get; set; }

        public string EmployeeName { get; set; } = string.Empty;

        [Required]
        public string ExpenseType { get; set; } = string.Empty;

        [Required]
        public decimal Amount { get; set; }

        public DateTime ExpenseDate { get; set; }

        public string Notes { get; set; } = string.Empty;
    }

    // =========================================================
    // SALARY SLIP
    // =========================================================
    public class SalarySlipViewModel
    {
        public string EmployeeName { get; set; } = string.Empty;

        public string Designation { get; set; } = string.Empty;

        public string Month { get; set; } = string.Empty;

        public decimal BasicSalary { get; set; }

        public decimal Bonus { get; set; }

        public decimal Deductions { get; set; }

        public decimal OvertimeAmount { get; set; }

        public decimal Expenses { get; set; }

        public decimal NetSalary { get; set; }

        public DateTime? PaymentDate { get; set; }
    }
}