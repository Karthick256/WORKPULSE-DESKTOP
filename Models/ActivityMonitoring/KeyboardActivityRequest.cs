using System;
using System.ComponentModel.DataAnnotations;

namespace monitor_desktop.Models.ActivityMonitoring
{
    public class KeyboardActivityRequest
    {
        [Required]
        public long SessionId { get; set; }
        [Required]
        public DateTime RecordedAt { get; set; }
        public int? IntervalSeconds { get; set; }
        public int? TotalKeystrokes { get; set; }
        public int? SpecialKeyCount { get; set; }
        public int? TypingBursts { get; set; }
        public int? AvgWpm { get; set; }
        public int? PeakWpm { get; set; }
        public int? ActiveTypingSeconds { get; set; }
    }
}