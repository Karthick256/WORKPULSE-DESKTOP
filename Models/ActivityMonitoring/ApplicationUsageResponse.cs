using System;
using monitor_desktop.Models.Enums;

namespace monitor_desktop.Models.ActivityMonitoring
{
    public class ApplicationUsageResponse
    {
        public long Id { get; set; }
        public string AppName { get; set; }
        public string AppPath { get; set; }
        public string AppVersion { get; set; }
        public AppCategory AppCategory { get; set; }
        public string WindowTitle { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public int DurationSeconds { get; set; }
        public int? FocusCount { get; set; }
        public bool? IsProductive { get; set; }
    }
}