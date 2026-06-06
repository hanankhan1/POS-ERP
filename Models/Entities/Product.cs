using System.Collections.Generic;

namespace POSERP.Models.Entities
{
    public class Product
    {
        public int ProductID { get; set; }
        public int CategoryID { get; set; }
        public int SupplierID { get; set; }
        public string ProductName { get; set; }
        public string Barcode { get; set; }
        public decimal CostPrice { get; set; }
        public decimal SellingPrice { get; set; }
        public int ReorderLevel { get; set; } = 10;
        public string Unit { get; set; }
        public string Status { get; set; } = "Available";
        public bool IsActive { get; set; } = true;

        // Navigation
        public virtual Category Category { get; set; }
        public virtual Supplier Supplier { get; set; }
        public virtual Inventory Inventory { get; set; }
        public virtual ICollection<SaleItem> SaleItems { get; set; }
        public virtual ICollection<PurchaseOrderItem> PurchaseOrderItems { get; set; }
    }
}