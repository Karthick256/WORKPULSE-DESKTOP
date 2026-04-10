using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using monitor_desktop.Services;

namespace monitor_desktop.ViewModels
{
    public class ForgotUsernameViewModel : INotifyPropertyChanged
    {
        private readonly AuthService _authService;
        private string _email;
        private string _errorMessage;
        private bool _hasError;
        private bool _isLoading;
        private bool _isSuccess;

        public string Email
        {
            get => _email;
            set { _email = value; OnPropertyChanged(); }
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

        public bool IsSuccess
        {
            get => _isSuccess;
            set { _isSuccess = value; OnPropertyChanged(); }
        }

        public ForgotUsernameViewModel()
        {
            var apiClient = new ApiClient();
            _authService = new AuthService(apiClient);
        }

        public async Task<bool> RecoverUsernameAsync()
        {
            if (string.IsNullOrWhiteSpace(Email))
            {
                ErrorMessage = "Please enter your email address";
                HasError = true;
                return false;
            }

            if (!IsValidEmail(Email))
            {
                ErrorMessage = "Please enter a valid email address";
                HasError = true;
                return false;
            }

            IsLoading = true;
            HasError = false;
            ErrorMessage = string.Empty;

            try
            {
                var request = new Models.AuthManagement.ForgotUsernameRequest { Email = Email };
                var response = await _authService.ForgotUsername(request);

                if (response.Status == 200)
                {
                    IsSuccess = true;
                    return true;
                }
                else
                {
                    ErrorMessage = response.Message ?? "Failed to recover username";
                    HasError = true;
                    return false;
                }
            }
            catch (System.Exception ex)
            {
                ErrorMessage = $"Request failed: {ex.Message}";
                HasError = true;
                return false;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}