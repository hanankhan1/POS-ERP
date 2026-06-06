using System.ComponentModel.DataAnnotations;

namespace POSERP.Models.Product
{
    public class ProductViewModel
    {
        public int ProductID { get; set; }

        [Required(ErrorMessage = "Category is required")]
        public int CategoryID { get; set; }

        [Required(ErrorMessage = "Supplier is required")]
        public int SupplierID { get; set; }

        [Required(ErrorMessage = "Product name is required")]
        [StringLength(150)]
        public string ProductName { get; set; }

        [Required(ErrorMessage = "Barcode is required")]
        [StringLength(100)]
        public string Barcode { get; set; }

        [Required(ErrorMessage = "Cost price is required")]
        [Range(0, 999999)]
        public decimal CostPrice { get; set; }

        [Required(ErrorMessage = "Selling price is required")]
        [Range(0, 999999)]
        public decimal SellingPrice { get; set; }

        [Required(ErrorMessage = "Reorder level is required")]
        [Range(0, 9999)]
        public int ReorderLevel { get; set; } = 10;

        public string Unit { get; set; } = string.Empty;

        public string Status { get; set; } = "Available";

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }

        // DISPLAY ONLY
        public string CategoryName { get; set; } = string.Empty;

        public string SupplierName { get; set; } = string.Empty;

        public int Stock { get; set; }
    }

    public class Category
    {
        public int CategoryID { get; set; }
        public string CategoryName { get; set; }
        public string Description { get; set; }
    }

    public class Supplier
    {
        public int SupplierID { get; set; }
        public string SupplierName { get; set; }
    }
}