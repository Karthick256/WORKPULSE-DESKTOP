using System;
using System.ComponentModel.DataAnnotations;
using monitor_desktop.Models.Enums;

namespace monitor_desktop.Models.ActivityMonitoring
{
    public class IdlePeriodRequest
    {
        [Required]
        public long SessionId { get; set; }
        [Required]
        public DateTime IdleStart { get; set; }
        public DateTime? IdleEnd { get; set; }
        [Required]
        public IdleTrigger TriggerReason { get; set; }
        public int? DurationSeconds { get; set; }
    }
}