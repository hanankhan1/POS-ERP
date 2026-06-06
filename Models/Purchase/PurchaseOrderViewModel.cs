using System.ComponentModel.DataAnnotations;

namespace POSERP.Models.Purchase
{
    // ========== FOR DISPLAY (with product names, barcodes) ==========
    public class PurchaseOrderViewModel
    {
        public int PurchaseOrderID { get; set; }

        [Required]
        public int SupplierID { get; set; }
        public string SupplierName { get; set; } = string.Empty;

        public DateTime OrderDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = "Pending";
        public List<PurchaseOrderItemViewModel> Items { get; set; } = new();
    }

    // Display model – includes ProductName and Barcode
    public class PurchaseOrderItemViewModel
    {
        public int ProductID { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal CostPrice { get; set; }
        public decimal SubTotal { get; set; }
    }

    // ========== FOR CREATE (POST) – no ProductName/Barcode ==========
    public class CreatePurchaseOrderViewModel
    {
        [Required(ErrorMessage = "Please select a supplier.")]
        public int SupplierID { get; set; }

        [Required(ErrorMessage = "At least one product is required.")]
        [MinLength(1, ErrorMessage = "Add at least one product.")]
        public List<CreatePurchaseOrderItemDto> Items { get; set; } = new();
    }

    // DTO for creating items – only fields needed for database insert
    public class CreatePurchaseOrderItemDto
    {
        [Required(ErrorMessage = "Select a product.")]
        public int ProductID { get; set; }

        [Required(ErrorMessage = "Quantity is required.")]
        [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1.")]
        public int Quantity { get; set; }

        [Required(ErrorMessage = "Cost price is required.")]
        [Range(0.01, 999999, ErrorMessage = "Cost price must be greater than 0.")]
        public decimal CostPrice { get; set; }
    }
}