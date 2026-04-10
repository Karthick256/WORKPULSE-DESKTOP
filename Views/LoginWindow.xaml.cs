using monitor_desktop.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace monitor_desktop.Views
{
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel _viewModel;

        public LoginWindow()
        {
            InitializeComponent();
            _viewModel = new LoginViewModel();
            DataContext = _viewModel;
        }

        // ── Existing methods (unchanged) ────────────────────────────────────

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.Password = PasswordBox.Password;

            var success = await _viewModel.LoginAsync();

            if (!success)
            {
                PasswordBox.Clear();
                PasswordBox.Focus();
            }
        }

        private async void ForgotUsername_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ForgotUsernameDialog();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private async void ForgotPassword_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ForgotPasswordDialog();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        // ── New handlers for custom title bar (required by new design) ──────

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}