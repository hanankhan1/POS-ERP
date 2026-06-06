using System;

namespace POSERP.Models.Entities
{
    public class SyncLog
    {
        public int SyncLogID { get; set; }
        public string DeviceID { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int RecordsSynced { get; set; }
        public string Status { get; set; }
        public string ErrorMessage { get; set; }
    }
}