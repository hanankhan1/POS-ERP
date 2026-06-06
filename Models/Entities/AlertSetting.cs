namespace POSERP.Models.Entities
{
    public class AlertSetting
    {
        public int SettingID { get; set; }
        public string SettingName { get; set; }
        public string SettingValue { get; set; }
        public string Description { get; set; }
        public string NotifyEmails { get; set; }
    }
}