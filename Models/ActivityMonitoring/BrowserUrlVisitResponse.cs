using monitor_desktop.Models.Enums;

namespace monitor_desktop.Models.ActivityMonitoring
{
    public class BrowserUrlVisitResponse
    {
        public long Id { get; set; }

        public string Url { get; set; }

        public string PageTitle { get; set; }

        public string Domain { get; set; }

        public UrlCategory Category { get; set; }

        public DateTime VisitedAt { get; set; }

        public int? TimeSpentSeconds { get; set; }

        public bool? IsProductive { get; set; }

        public int? VisitCount { get; set; }
    }
}