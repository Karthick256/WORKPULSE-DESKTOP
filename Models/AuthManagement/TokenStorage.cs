namespace monitor_desktop.Models.AuthManagement
{
    public class TokenStorage
    {
        public string Token { get; set; }
        public string Type { get; set; } = "Bearer";
        public List<string> Roles { get; set; }
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }

        public bool IsValid() => !string.IsNullOrEmpty(Token) && DateTime.Now < ExpiresAt;
        public bool HasRole(string role) => Roles?.Contains(role) == true;
        public bool IsAdmin => HasRole("ADMIN");
    }
}