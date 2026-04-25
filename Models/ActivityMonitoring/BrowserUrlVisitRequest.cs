using System;
using System.ComponentModel.DataAnnotations;
using monitor_desktop.Models.Enums;

namespace monitor_desktop.Models.ActivityMonitoring
{
    public class BrowserUrlVisitRequest
    {
        [Required]
        public long SessionId { get; set; }  // NEW: Direct session reference

        // Remove or make optional - BrowserUsageId is no longer required
        public long? BrowserUsageId { get; set; }  // Optional - can be removed entirely

        [Required]
        public string Url { get; set; }

        public string PageTitle { get; set; }

        [Required]
        public string Domain { get; set; }

        public UrlCategory Category { get; set; }

        [Required]
        public DateTime VisitedAt { get; set; }

        public int? TimeSpentSeconds { get; set; }

        public bool? IsProductive { get; set; }

        public int? VisitCount { get; set; }
    }
}