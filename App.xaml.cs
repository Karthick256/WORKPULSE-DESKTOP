using System.Windows;
using monitor_desktop.Services;
using monitor_desktop.Views;

namespace monitor_desktop
{
    public partial class App : Application
    {
        private readonly TokenManager _tokenManager;
        public App()
        {
            _tokenManager = new TokenManager();
        }
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            if (_tokenManager.IsAuthenticated)
            {
                var dashboard = new DashboardWindow();
                dashboard.Show();
            }
            else
            {
                var loginWindow = new LoginWindow();
                loginWindow.Show();
            }
        }
    }
}