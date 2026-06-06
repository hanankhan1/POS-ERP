namespace POSERP.Models.Fraud
{
    public class FraudAlertViewModel
    {
        public int AlertID { get; set; }
        public int SaleID { get; set; }
        public string AlertType { get; set; } = string.Empty;
        public string RiskLevel { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; } = string.Empty; // Pending, Dismissed, Investigated
        public string CashierName { get; set; } = string.Empty;
        public decimal SaleAmount { get; set; }
    }

    public class AlertSettingViewModel
    {
        public int SettingID { get; set; }
        public string SettingName { get; set; } = string.Empty;
        public string SettingValue { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string NotifyEmails { get; set; } = string.Empty;
    }
}