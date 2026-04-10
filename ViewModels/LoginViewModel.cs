using monitor_desktop.Models.AuthManagement;
using monitor_desktop.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;

namespace monitor_desktop.ViewModels
{
    public class LoginViewModel : INotifyPropertyChanged
    {
        private readonly AuthService _authService;
        private readonly TokenManager _tokenManager;

        private string _username;
        private string _password;
        private string _errorMessage;
        private bool _hasError;
        private bool _isLoading;

        public string Username
        {
            get => _username;
            set { _username = value; OnPropertyChanged(); }
        }

        public string Password
        {
            get => _password;
            set { _password = value; OnPropertyChanged(); }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        public bool HasError
        {
            get => _hasError;
            set { _hasError = value; OnPropertyChanged(); }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        public LoginViewModel()
        {
            var apiClient = new ApiClient();
            _authService = new AuthService(apiClient);
            _tokenManager = new TokenManager();

            // Check for existing session
            if (_tokenManager.IsAuthenticated)
            {
                NavigateToDashboard();
            }
        }

        public async Task<bool> LoginAsync()
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Please enter both username and password";
                HasError = true;
                return false;
            }

            IsLoading = true;
            HasError = false;
            ErrorMessage = string.Empty;

            try
            {
                var request = new LoginRequest
                {
                    Username = Username,
                    Password = Password
                };

                var response = await _authService.SignIn(request);

                if (response.Status == 200 && response.Data != null)
                {
                    // Token is already saved in AuthService.SignIn
                    NavigateToDashboard();
                    return true;
                }
                else
                {
                    ErrorMessage = response.Message ?? "Invalid username or password";
                    HasError = true;
                    return false;
                }
            }
            catch (System.Exception ex)
            {
                ErrorMessage = $"Login failed: {ex.Message}";
                HasError = true;
                return false;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void NavigateToDashboard()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var dashboard = new Views.DashboardWindow();
                dashboard.Show();
                Application.Current.Windows[0]?.Close();
            });
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}