namespace monitor_desktop.Models.AuthManagement
{
    public class TokenStorage
    {
        public string Token { get; set; }

        public string Type { get; set; } = "Bearer";

        public List<string> Roles { get; set; }

        public DateTime IssuedAt { get; set; }

        public DateTime ExpiresAt { get; set; }

        public bool IsValid()
        {
            return !string.IsNullOrEmpty(Token) && DateTime.Now < ExpiresAt;
        }

        public bool HasRole(string role)
        {
            return Roles?.Contains(role) == true;
        }

        public bool IsAdmin => HasRole("ADMIN");

        public bool IsManager => HasRole("MANAGER");

        public bool IsEmployee => HasRole("EMPLOYEE");
    }
}