using System;
using System.ComponentModel.DataAnnotations;
using monitor_desktop.Models.Enums;

namespace monitor_desktop.Models.ActivityMonitoring
{
    public class BrowserUrlVisitRequest
    {
        public long? BrowserUsageId { get; set; }
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