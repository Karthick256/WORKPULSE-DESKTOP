using monitor_desktop.Models.Enums;

namespace monitor_desktop.Models.ActivityMonitoring
{
    public class BreakResponse
    {
        public long Id { get; set; }
        public DateTime BreakStart { get; set; }
        public DateTime? BreakEnd { get; set; }
        public int DurationSeconds { get; set; }
        public BreakType BreakType { get; set; }
        public BreakTrigger TriggerReason { get; set; }
        public string Notes { get; set; }
        public bool IsPlanned { get; set; }
    }
}