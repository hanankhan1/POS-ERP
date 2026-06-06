using System;

namespace POSERP.Models.Entities
{
    public class Inventory
    {
        public int InventoryID { get; set; }
        public int ProductID { get; set; }
        public int QuantityInStock { get; set; } = 0;
        public DateTime LastUpdated { get; set; } = DateTime.Now;

        public virtual Product Product { get; set; }
    }
}