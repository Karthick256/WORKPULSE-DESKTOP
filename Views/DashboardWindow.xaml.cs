using System;
using System.Windows;
using System.Windows.Input;
using monitor_desktop.Services;
using monitor_desktop.ViewModels;

namespace monitor_desktop.Views
{
    public partial class DashboardWindow : Window
    {
        private readonly DashboardViewModel _viewModel;
        private AutoUpdateService _updateService;

        public DashboardWindow()
        {
            InitializeComponent();
            _viewModel = new DashboardViewModel();
            DataContext = _viewModel;
            _updateService = new AutoUpdateService();
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to logout?", "Confirm Logout",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _viewModel.Logout();
                var loginWindow = new LoginWindow();
                loginWindow.Show();
                Close();
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            if (_updateService != null)
            {
                await _updateService.CheckForUpdatesAsync(silent: false);
            }
        }

        // Window Control Handlers
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Normal)
            {
                WindowState = WindowState.Maximized;
                MaximizeButton.Content = "❐"; // Restore icon
            }
            else
            {
                WindowState = WindowState.Normal;
                MaximizeButton.Content = "□"; // Maximize icon
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to exit WorkPulse?", "Exit Application",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Application.Current.Shutdown();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _updateService?.Dispose();
            base.OnClosed(e);
        }
    }
}