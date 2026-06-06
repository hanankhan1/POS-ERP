using System;

namespace POSERP.Models.Entities
{
    public class FraudAlert
    {
        public int AlertID { get; set; }
        public int SaleID { get; set; }
        public bool GeneratedByAI { get; set; } = true;
        public string AlertType { get; set; }
        public string RiskLevel { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string Status { get; set; }

        public virtual Sale Sale { get; set; }
    }
}