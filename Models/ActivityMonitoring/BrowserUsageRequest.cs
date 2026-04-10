using System.ComponentModel.DataAnnotations;

namespace monitor_desktop.Models.ActivityMonitoring
{
    public class BrowserUsageRequest
    {
        [Required]
        public long SessionId { get; set; }

        [Required]
        public string BrowserName { get; set; }

        public string BrowserVersion { get; set; }

        [Required]
        public DateTime StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        public int? DurationSeconds { get; set; }
    }
}