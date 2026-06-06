using System;

namespace POSERP.Models.Entities
{
    public class OfflineTransaction
    {
        public int OfflineID { get; set; }
        public string DeviceID { get; set; }
        public string TransactionData { get; set; } // JSON
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? SyncedAt { get; set; }
        public string SyncStatus { get; set; } = "Pending";
    }
}