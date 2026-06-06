using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace POSERP.Models.Customer
{
    public class CustomerViewModel
    {
        public int CustomerID { get; set; }

        [Required(ErrorMessage = "Full name is required")]
        [StringLength(100, ErrorMessage = "Full name cannot exceed 100 characters.")]
        public string FullName { get; set; }

        [Required(ErrorMessage = "Phone number is required")]
        [StringLength(20, ErrorMessage = "Phone number cannot exceed 20 characters.")]
        [RegularExpression(@"^[0-9]+$", ErrorMessage = "Phone must contain only digits")]
        public string Phone { get; set; }

        [StringLength(255, ErrorMessage = "Address cannot exceed 255 characters.")]
        public string Address { get; set; }

        public int LoyaltyPoints { get; set; }

        // Adjusted range upper boundary to match the full extent of a DECIMAL(10,2) space
        [Range(0.00, 99999999.99, ErrorMessage = "Credit Balance must be a valid positive currency amount up to 8 digits.")]
        public decimal CreditBalance { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class CustomerPurchaseHistoryViewModel
    {
        public int CustomerID { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public int LoyaltyPoints { get; set; }
        public decimal CreditBalance { get; set; }
        public List<CustomerSale> Sales { get; set; } = new();
    }

    public class CustomerSale
    {
        public int SaleId { get; set; }
        public DateTime SaleDate { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = string.Empty;
        public bool IsCancelled { get; set; }
        public List<SaleItemInfo> Items { get; set; } = new();
    }

    public class SaleItemInfo
    {
        public int ProductID { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Discount { get; set; }
        public decimal SubTotal { get; set; }
    }
}