using System;
using System.Collections.Generic;

namespace POSERP.Models.Entities
{
    public class User
    {
        public int UserID { get; set; }
        public string Role { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public string Phone { get; set; }
        public string Status { get; set; } = "Active";
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation
        public virtual Employee Employee { get; set; }
        public virtual ICollection<AuditLog> AuditLogs { get; set; }
    }
}