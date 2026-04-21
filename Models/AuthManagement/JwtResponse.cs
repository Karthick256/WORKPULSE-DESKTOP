using System;
using System.Collections.Generic;

namespace monitor_desktop.Models.AuthManagement
{
    public class JwtResponse
    {
        public string Token { get; set; }
        public string Type { get; set; } = "Bearer";
        public List<string> Roles { get; set; }
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}