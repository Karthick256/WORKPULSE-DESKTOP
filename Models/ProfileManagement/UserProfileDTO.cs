using monitor_desktop.Models.AuthManagement;

namespace monitor_desktop.Models.ProfileManagement
{
    public class UserProfileDTO
    {
        public long Id { get; set; }

        public string Username { get; set; }

        public string Email { get; set; }

        public string FirstName { get; set; }

        public string LastName { get; set; }

        public string Phone { get; set; }

        public AddressDTO Address { get; set; }

        public ProfileImageDTO ProfileImage { get; set; }

        public bool EmailVerified { get; set; }

        public HashSet<string> Roles { get; set; }

        public bool Status { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public int Version { get; set; }
    }
}