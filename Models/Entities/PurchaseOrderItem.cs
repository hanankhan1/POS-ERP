namespace POSERP.Models.Entities
{
    public class PurchaseOrderItem
    {
        public int POItemID { get; set; }
        public int PurchaseOrderID { get; set; }
        public int ProductID { get; set; }
        public int Quantity { get; set; }
        public decimal CostPrice { get; set; }
        public decimal SubTotal { get; set; }

        public virtual PurchaseOrder PurchaseOrder { get; set; }
        public virtual Product Product { get; set; }
    }
}