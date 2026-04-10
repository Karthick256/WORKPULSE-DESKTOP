using monitor_desktop.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace monitor_desktop.Models.ActivityMonitoring
{
    public class ApplicationUsageRequest
    {
        [Required]
        public long SessionId { get; set; }

        [Required]
        public string AppName { get; set; }

        public string AppPath { get; set; }

        public string AppVersion { get; set; }

        public AppCategory AppCategory { get; set; }

        public string WindowTitle { get; set; }

        [Required]
        public DateTime StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        [Required]
        public int DurationSeconds { get; set; }

        public int? FocusCount { get; set; }

        public bool? IsProductive { get; set; }
    }
}