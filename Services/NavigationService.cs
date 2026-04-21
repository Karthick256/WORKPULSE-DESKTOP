using System.Windows;
using monitor_desktop.Views;

namespace monitor_desktop.Services
{
    public class NavigationService
    {
        public void NavigateToLogin()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var loginWindow = new LoginWindow();
                loginWindow.Show();
                CloseCurrentWindow();
            });
        }

        public void NavigateToDashboard()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var dashboard = new DashboardWindow();
                dashboard.Show();
                CloseCurrentWindow();
            });
        }

        private void CloseCurrentWindow()
        {
            if (Application.Current.Windows.Count > 0)
            {
                var currentWindow = Application.Current.Windows[0];
                if (currentWindow != null && currentWindow.IsVisible)
                {
                    currentWindow.Close();
                }
            }
        }
    }
}