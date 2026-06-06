using System;

namespace POSERP.Models.Entities
{
    public class Payment
    {
        public int PaymentID { get; set; }
        public int SaleID { get; set; }
        public DateTime PaymentDate { get; set; } = DateTime.Now;
        public decimal AmountPaid { get; set; }
        public string PaymentMethod { get; set; }
        public string TransactionReference { get; set; }

        public virtual Sale Sale { get; set; }
    }
}