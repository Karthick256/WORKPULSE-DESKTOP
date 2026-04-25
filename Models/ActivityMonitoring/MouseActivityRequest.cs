using System.ComponentModel.DataAnnotations;

namespace monitor_desktop.Models.ActivityMonitoring
{
    public class MouseActivityRequest
    {
        [Required]
        public long SessionId { get; set; }
        [Required]
        public DateTime RecordedAt { get; set; }
        public int? IntervalSeconds { get; set; }
        public int? TotalClicks { get; set; }
        public int? LeftClicks { get; set; }
        public int? RightClicks { get; set; }
        public int? MiddleClicks { get; set; }
        public int? DoubleClicks { get; set; }
        public int? ScrollEvents { get; set; }
        public long? DistancePixels { get; set; }
    }
}