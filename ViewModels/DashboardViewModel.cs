using System.ComponentModel;
using System.Runtime.CompilerServices;
using monitor_desktop.Services;

namespace monitor_desktop.ViewModels
{
    public class DashboardViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly TokenManager _tokenManager;
        private string _welcomeMessage;
        private string _currentTime;
        private string _username;
        private string _userInitials;
        private string _userRoles;
        private bool _isAdmin;
        private System.Timers.Timer _timer;

        public string WelcomeMessage
        {
            get => _welcomeMessage;
            set { _welcomeMessage = value; OnPropertyChanged(); }
        }

        public string CurrentTime
        {
            get => _currentTime;
            set { _currentTime = value; OnPropertyChanged(); }
        }

        public string Username
        {
            get => _username;
            set
            {
                _username = value;
                OnPropertyChanged();
                // Auto-derive initials whenever Username changes
                UserInitials = BuildInitials(value);
            }
        }

        public string UserInitials
        {
            get => _userInitials;
            set { _userInitials = value; OnPropertyChanged(); }
        }

        public string UserRoles
        {
            get => _userRoles;
            set { _userRoles = value; OnPropertyChanged(); }
        }

        public bool IsAdmin
        {
            get => _isAdmin;
            set { _isAdmin = value; OnPropertyChanged(); }
        }

        public DashboardViewModel()
        {
            _tokenManager = new TokenManager();
            LoadUserInfo();
            UpdateTime();

            _timer = new System.Timers.Timer(1000);
            _timer.Elapsed += (s, e) => UpdateTime();
            _timer.Start();
        }

        private void LoadUserInfo()
        {
            var token = _tokenManager.CurrentToken;
            if (token != null)
            {
                Username = "Employee";   // Replace with real name from token when available
                UserRoles = string.Join(", ", token.Roles ?? new System.Collections.Generic.List<string>());
                IsAdmin = token.IsAdmin;
                WelcomeMessage = $"Welcome back!";
            }
            else
            {
                Username = "Guest";
                WelcomeMessage = "Welcome!";
            }
        }

        /// <summary>
        /// Builds up to 2 uppercase initials from a display name.
        /// "John Doe" → "JD",  "Employee" → "E",  null/empty → "?"
        /// </summary>
        private static string BuildInitials(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "?";

            var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
                return parts[0][0].ToString().ToUpper();

            return $"{parts[0][0]}{parts[^1][0]}".ToUpper();
        }

        private void UpdateTime()
        {
            CurrentTime = DateTime.Now.ToString("dddd, MMMM dd, yyyy  HH:mm:ss");
        }

        public void Logout()
        {
            var apiClient = new ApiClient(_tokenManager);
            var authService = new AuthService(apiClient);
            authService.Logout();
            _timer?.Stop();
        }

        public void Dispose()
        {
            _timer?.Stop();
            _timer?.Dispose();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}