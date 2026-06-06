using System;

namespace POSERP.Models.Entities
{
    public class AuditLog
    {
        public int LogID { get; set; }
        public int UserID { get; set; }
        public string Action { get; set; }
        public string TableName { get; set; }
        public int? RecordID { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public virtual User User { get; set; }
    }
}