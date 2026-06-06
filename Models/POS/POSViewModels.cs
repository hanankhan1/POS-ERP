namespace POSERP.Models.ViewModels
{
    public class ProductSearchViewModel
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public int StockQuantity { get; set; }
        public decimal SellingPrice { get; set; }
    }

    public class CartItemViewModel
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Total => Quantity * Price;
    }

    public class SaleViewModel
    {
        public int CustomerId { get; set; }
        public string PaymentMethod { get; set; } = "Cash";
        public decimal DiscountPercentage { get; set; }
        public decimal TaxPercentage { get; set; } = 8;
        public List<CartItemViewModel> CartItems { get; set; } = new List<CartItemViewModel>();
    }

    public class ReceiptViewModel
    {
        public int SaleId { get; set; }
        public DateTime Date { get; set; }
        public string CustomerName { get; set; } = "Walk-in Customer";
        public string CashierName { get; set; } = string.Empty;
        public List<CartItemViewModel> Items { get; set; } = new();
        public decimal Subtotal { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TaxPercent { get; set; }
        public decimal TotalAmount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public string ReceiptNumber { get; set; } = string.Empty;
    }
}