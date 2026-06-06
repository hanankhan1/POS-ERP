using System;
using System.Collections.Generic;

namespace POSERP.Models.Entities
{
    public class Sale
    {
        public int SaleID { get; set; }
        public int? CustomerID { get; set; }
        public int CashierID { get; set; }
        public DateTime SaleDate { get; set; } = DateTime.Now;
        public decimal TotalAmount { get; set; }
        public decimal DiscountAmount { get; set; } = 0;
        public decimal TaxAmount { get; set; } = 0;
        public string PaymentMethod { get; set; }
        public string PaymentStatus { get; set; }
        public bool IsCancelled { get; set; } = false;
        public string CancellationReason { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string CancelledBy { get; set; }

        // Navigation
        public virtual Customer Customer { get; set; }
        public virtual User Cashier { get; set; }
        public virtual ICollection<SaleItem> SaleItems { get; set; }
        public virtual ICollection<Payment> Payments { get; set; }
        public virtual ICollection<FraudAlert> FraudAlerts { get; set; }
        public virtual Receipt Receipt { get; set; }
    }
}