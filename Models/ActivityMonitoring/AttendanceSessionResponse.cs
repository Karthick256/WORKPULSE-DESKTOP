using System;
using monitor_desktop.Models.Enums;

namespace monitor_desktop.Models.ActivityMonitoring
{
    public class AttendanceSessionResponse
    {
        public long? SessionId { get; set; }
        public long? UserId { get; set; }
        public string Username { get; set; }
        public DateTime? WorkDate { get; set; }
        public DateTime? CheckInTime { get; set; }
        public DateTime? CheckOutTime { get; set; }
        public int? TotalActiveMinutes { get; set; }
        public int? TotalIdleMinutes { get; set; }
        public int? TotalBreakMinutes { get; set; }
        public int? TotalSessionMinutes { get; set; }
        public SessionStatus SessionStatus { get; set; }
        public string WorkstationName { get; set; }
        public string IpAddress { get; set; }
        public string OsInfo { get; set; }
        public string MacAddress { get; set; }
        public float? ProductivityScore { get; set; }
    }
}