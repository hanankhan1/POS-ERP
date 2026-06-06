using System;
using System.Collections.Generic;

namespace POSERP.Models.Entities
{
    public class PurchaseOrder
    {
        public int PurchaseOrderID { get; set; }
        public int SupplierID { get; set; }
        public int CreatedBy { get; set; }
        public DateTime OrderDate { get; set; } = DateTime.Now;
        public decimal? TotalAmount { get; set; }
        public string Status { get; set; }

        public virtual Supplier Supplier { get; set; }
        public virtual User CreatedByUser { get; set; }
        public virtual ICollection<PurchaseOrderItem> PurchaseOrderItems { get; set; }
    }
}