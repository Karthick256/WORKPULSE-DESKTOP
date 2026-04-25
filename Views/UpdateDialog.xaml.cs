using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using monitor_desktop.Services;

namespace monitor_desktop.Views
{
    public partial class UpdateDialog : Window, INotifyPropertyChanged
    {
        private readonly VersionManager _versionManager;
        private readonly UpdateInfo _updateInfo;

        private bool _isDownloading;
        private int _downloadProgress;
        private string _statusMessage;
        private bool _canClose;

        public bool IsDownloading
        {
            get => _isDownloading;
            set { _isDownloading = value; OnPropertyChanged(); }
        }

        public int DownloadProgress
        {
            get => _downloadProgress;
            set { _downloadProgress = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public bool CanClose
        {
            get => _canClose;
            set { _canClose = value; OnPropertyChanged(); }
        }

        public UpdateDialog(UpdateInfo updateInfo)
        {
            InitializeComponent();
            DataContext = this;

            _versionManager = new VersionManager();
            _updateInfo = updateInfo;
            CanClose = true;

            LoadUpdateInfo();
        }

        private void LoadUpdateInfo()
        {
            VersionText.Text = $"Current: {_updateInfo.CurrentVersion.Version} → New: {_updateInfo.LatestVersion.Version}";

            if (!string.IsNullOrEmpty(_updateInfo.LatestVersion.ReleaseNotes))
            {
                ReleaseNotesText.Text = _updateInfo.LatestVersion.ReleaseNotes;
            }
            else
            {
                ReleaseNotesText.Text = "No release notes available.";
            }

            if (_updateInfo.LatestVersion.IsMandatory)
            {
                
                Title = "Mandatory Update Required";
                StatusMessage = "This is a mandatory update. The application will update automatically.";
            }
        }

        private async void InstallNow_Click(object sender, RoutedEventArgs e)
        {
            IsDownloading = true;
            CanClose = false;
            InstallButton.IsEnabled = false;
            
            RemindLaterButton.IsEnabled = false;

            var progress = new Progress<int>(p =>
            {
                DownloadProgress = p;
                StatusMessage = p < 100 ? $"Downloading update... {p}%" : "Installing update...";
            });

            try
            {
                var success = await _versionManager.DownloadAndInstallUpdateAsync(_updateInfo.LatestVersion, progress);

                if (success)
                {
                    StatusMessage = "Update installed! Restarting application...";

                    var restartScript = CreateRestartScript();
                    var scriptPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "workpulse_restart.bat");
                    System.IO.File.WriteAllText(scriptPath, restartScript);

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = scriptPath,
                        UseShellExecute = true,
                        CreateNoWindow = true
                    });

                    Application.Current.Shutdown();
                }
                else
                {
                    StatusMessage = "Update failed. Please try again later or download manually.";
                    IsDownloading = false;
                    CanClose = true;
                    InstallButton.IsEnabled = true;
                    if (!_updateInfo.LatestVersion.IsMandatory)
                    {
                        
                        RemindLaterButton.IsEnabled = true;
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Update error: {ex.Message}";
                IsDownloading = false;
                CanClose = true;
                InstallButton.IsEnabled = true;
                if (!_updateInfo.LatestVersion.IsMandatory)
                {
                    
                    RemindLaterButton.IsEnabled = true;
                }
            }
        }

        private string CreateRestartScript()
        {
            var exePath = Assembly.GetExecutingAssembly().Location;
            return $@"
@echo off
timeout /t 2 /nobreak > nul
start """" ""{exePath}""
exit
";
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Skipping this update may cause compatibility issues. Are you sure?",
                "Skip Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                DialogResult = false;
                Close();
            }
        }

        private void RemindLater_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}