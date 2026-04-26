using System.Windows;
using monitor_desktop.Services;
using monitor_desktop.Views;

namespace monitor_desktop
{
    public partial class App : Application
    {
        private readonly TokenManager _tokenManager;
        private static Mutex _appMutex;
        private AutoUpdateService _updateService;

        public App()
        {
            _tokenManager = new TokenManager();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            bool createdNew;
            _appMutex = new Mutex(true, "WorkPulse_Application", out createdNew);

            if (!createdNew)
            {
                var existingWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault();
                if (existingWindow != null)
                {
                    existingWindow.WindowState = WindowState.Normal;
                    existingWindow.Activate();
                }
                Current.Shutdown();
                return;
            }

            _updateService = new AutoUpdateService();
            _updateService.StartBackgroundChecking();

            var versionManager = new VersionManager();
            versionManager.CleanupOldUpdates();

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
            _updateService?.Dispose();

            var tracker = ActivityTrackerService.GetExistingInstance();
            if (tracker != null && tracker.IsTracking)
            {
                tracker.StopTrackingAsync(true).Wait(500);
            }
            ActivityTrackerService.DisposeInstance();
            _appMutex?.ReleaseMutex();
            _appMutex?.Dispose();
            base.OnExit(e);
        }
    }
}