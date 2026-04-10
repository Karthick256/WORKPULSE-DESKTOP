namespace monitor_desktop.Models.ActivityMonitoring
{
    public class ProductivitySummaryResponse
    {
        public long Id { get; set; }

        public long UserId { get; set; }

        public string Username { get; set; }

        public DateTime SummaryDate { get; set; }

        public int? TotalWorkMinutes { get; set; }

        public int? ProductiveMinutes { get; set; }

        public int? IdleMinutes { get; set; }

        public int? BreakMinutes { get; set; }

        public float? ProductivityScore { get; set; }

        public string TopApplication { get; set; }

        public string TopDomain { get; set; }

        public int? TotalKeystrokes { get; set; }

        public int? TotalMouseClicks { get; set; }

        public int? SessionCount { get; set; }

        public int? TotalApplicationsUsed { get; set; }

        public int? TotalUrlsVisited { get; set; }
    }
}