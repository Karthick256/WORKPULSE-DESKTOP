using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using monitor_desktop.Views;
using Microsoft.Win32;

namespace monitor_desktop.Services
{
    public class AutoUpdateService : IDisposable
    {
        private readonly VersionManager _versionManager;
        private DispatcherTimer _updateCheckTimer;
        private bool _isChecking;
        private DateTime _lastCheckTime;
        private DateTime _nextReminderTime;

        // Configuration
        private const int CHECK_INTERVAL_HOURS = 6; // Check every 6 hours
        private const int REMINDER_DELAY_HOURS = 24; // Remind after 24 hours if skipped

        public event EventHandler<UpdateInfo> UpdateFound;

        public AutoUpdateService()
        {
            _versionManager = new VersionManager();
            LoadSettings();
        }

        private void LoadSettings()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\WorkPulse\AutoUpdate");
                if (key != null)
                {
                    var lastCheckStr = key.GetValue("LastCheckTime") as string;
                    if (DateTime.TryParse(lastCheckStr, out var lastCheck))
                        _lastCheckTime = lastCheck;

                    var nextReminderStr = key.GetValue("NextReminderTime") as string;
                    if (DateTime.TryParse(nextReminderStr, out var nextReminder))
                        _nextReminderTime = nextReminder;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load update settings: {ex.Message}");
            }
        }

        private void SaveSettings()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\WorkPulse\AutoUpdate");
                if (key != null)
                {
                    key.SetValue("LastCheckTime", _lastCheckTime.ToString("O"));
                    key.SetValue("NextReminderTime", _nextReminderTime.ToString("O"));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save update settings: {ex.Message}");
            }
        }

        public void StartBackgroundChecking()
        {
            if (_updateCheckTimer != null)
                return;

            _updateCheckTimer = new DispatcherTimer();
            _updateCheckTimer.Interval = TimeSpan.FromHours(CHECK_INTERVAL_HOURS);
            _updateCheckTimer.Tick += async (s, e) => await CheckForUpdatesAsync();
            _updateCheckTimer.Start();

            // Also check on startup
            Application.Current.Dispatcher.BeginInvoke(async () => await CheckForUpdatesAsync());

            Debug.WriteLine("Auto-update background checking started");
        }

        public void StopBackgroundChecking()
        {
            _updateCheckTimer?.Stop();
            _updateCheckTimer = null;
        }

        public async Task CheckForUpdatesAsync(bool silent = true)
        {
            if (_isChecking)
                return;

            // Check if we should remind later
            if (!silent && _nextReminderTime > DateTime.Now)
            {
                Debug.WriteLine($"Update reminder skipped until {_nextReminderTime}");
                return;
            }

            _isChecking = true;

            try
            {
                var updateInfo = await _versionManager.CheckForUpdatesAsync();

                if (updateInfo.HasUpdate)
                {
                    _lastCheckTime = DateTime.Now;
                    SaveSettings();

                    Debug.WriteLine($"Update found: {updateInfo.LatestVersion.Version}");

                    // Notify UI
                    UpdateFound?.Invoke(this, updateInfo);

                    // Show update dialog
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var dialog = new UpdateDialog(updateInfo);
                        var result = dialog.ShowDialog();

                        // If user clicked "Remind Later"
                        if (result == false && !updateInfo.LatestVersion.IsMandatory)
                        {
                            _nextReminderTime = DateTime.Now.AddHours(REMINDER_DELAY_HOURS);
                            SaveSettings();
                        }
                    });
                }
                else if (!silent)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show(
                            "You are running the latest version of WorkPulse.",
                            "No Updates Available",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update check failed: {ex.Message}");
                if (!silent)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show(
                            $"Failed to check for updates: {ex.Message}\n\nPlease check your internet connection.",
                            "Update Check Failed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    });
                }
            }
            finally
            {
                _isChecking = false;
            }
        }

        public async Task ManualCheckAsync()
        {
            await CheckForUpdatesAsync(silent: false);
        }

        public void Dispose()
        {
            StopBackgroundChecking();
        }
    }
}