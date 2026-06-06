using System;
using System.Collections.Generic;

namespace POSERP.Models.Entities
{
    public class Employee
    {
        public int EmployeeID { get; set; }
        public int UserID { get; set; }
        public string CNIC { get; set; }
        public DateTime? HireDate { get; set; }
        public decimal? Salary { get; set; }
        public string Designation { get; set; }

        public virtual User User { get; set; }
        public virtual ICollection<Attendance> Attendances { get; set; }
        public virtual ICollection<Payroll> Payrolls { get; set; }
        public virtual ICollection<Performance> Performances { get; set; }
    }
}