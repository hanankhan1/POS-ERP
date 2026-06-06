using System.Collections.Generic;

namespace POSERP.Models.Entities
{
    public class Customer
    {
        public int CustomerID { get; set; }
        public string FullName { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        public int LoyaltyPoints { get; set; } = 0;
        public decimal CreditBalance { get; set; } = 0;

        public virtual ICollection<Sale> Sales { get; set; }
    }
}