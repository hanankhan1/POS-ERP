using System;

namespace POSERP.Models.Entities
{
    public class Receipt
    {
        public int ReceiptID { get; set; }
        public int SaleID { get; set; }
        public string ReceiptNumber { get; set; }
        public string ReceiptHTML { get; set; }
        public byte[] ReceiptPDF { get; set; }
        public DateTime GeneratedAt { get; set; } = DateTime.Now;

        public virtual Sale Sale { get; set; }
    }
}