using monitor_desktop.Models.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace monitor_desktop.Models.ActivityMonitoring
{
    public class BreakStartRequest
    {
        [Required]
        public long SessionId { get; set; }
        public BreakType BreakType { get; set; }
        public BreakTrigger TriggerReason { get; set; }
        public string Notes { get; set; }
        public bool IsPlanned { get; set; }
    }
}
