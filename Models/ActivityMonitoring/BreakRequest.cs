using System.ComponentModel.DataAnnotations;
using monitor_desktop.Models.Enums;

namespace monitor_desktop.Models.ActivityMonitoring
{
    public class BreakRequest
    {
        [Required]
        public long SessionId { get; set; }
        [Required]
        public DateTime BreakStart { get; set; }
        public DateTime? BreakEnd { get; set; }
        [Required]
        public BreakType BreakType { get; set; }
        public BreakTrigger TriggerReason { get; set; }
        public string Notes { get; set; }
        public bool? IsPlanned { get; set; }
        public int? DurationSeconds { get; set; }
    }
}