using System.ComponentModel.DataAnnotations;

namespace monitor_desktop.Models.ActivityMonitoring
{
    public class CheckInRequest
    {
        [Required]
        public string WorkstationName { get; set; }
        [Required]
        public string IpAddress { get; set; }
        [Required]
        public string OsInfo { get; set; }
        public string MacAddress { get; set; }
    }
}