using System.ComponentModel.DataAnnotations;

namespace monitor_desktop.Models.ActivityMonitoring
{
    public class ActivityBatchRequest
    {
        [Required]
        public long SessionId { get; set; }

        public List<ApplicationUsageRequest> Applications { get; set; } = new();

        public List<BrowserUsageRequest> Browsers { get; set; } = new();

        public List<BrowserUrlVisitRequest> UrlVisits { get; set; } = new();

        public MouseActivityRequest MouseSnapshot { get; set; }

        public KeyboardActivityRequest KeyboardSnapshot { get; set; }

        public IdlePeriodRequest IdlePeriod { get; set; }
    }
}