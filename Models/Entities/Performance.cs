using System;

namespace POSERP.Models.Entities
{
    public class Performance
    {
        public int PerformanceID { get; set; }
        public int EmployeeID { get; set; }
        public DateTime ReviewDate { get; set; }
        public decimal? SalesTarget { get; set; }
        public decimal? SalesAchieved { get; set; }
        public int? CustomerRating { get; set; }
        public string Comments { get; set; }

        public virtual Employee Employee { get; set; }
    }
}