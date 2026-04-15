using System.Windows;
using monitor_desktop.Services;
using monitor_desktop.Views;

namespace monitor_desktop
{
    public partial class App : Application
    {
        private readonly TokenManager _tokenManager;
        private static Mutex _appMutex;

        public App()
        {
            _tokenManager = new TokenManager();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _appMutex = new Mutex(true, "WorkPulse_Application", out bool isNewInstance);

            if (!isNewInstance)
            {
                Current.Shutdown();
                return;
            }

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

        protected override void OnExit(ExitEventArgs e)
        {
            var tracker = ServiceLocator.GetExistingTrackerService();
            if (tracker != null && tracker.IsTracking)
            {
                tracker.StopTracking();
                Thread.Sleep(500);
            }
            ServiceLocator.DisposeTrackerService();
            _appMutex?.ReleaseMutex();
            _appMutex?.Dispose();
            base.OnExit(e);
        }
    }
}