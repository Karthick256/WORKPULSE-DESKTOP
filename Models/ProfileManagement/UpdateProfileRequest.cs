using monitor_desktop.Models.AuthManagement;

namespace monitor_desktop.Models.ProfileManagement
{
    public class UpdateProfileRequest
    {
        public string FirstName { get; set; }

        public string LastName { get; set; }

        public string Username { get; set; }

        public string Email { get; set; }

        public string Phone { get; set; }

        public AddressDTO Address { get; set; }
    }
}