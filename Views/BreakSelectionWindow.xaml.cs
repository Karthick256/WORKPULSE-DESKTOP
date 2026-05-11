using System.Windows;
using monitor_desktop.Models.Enums;

namespace monitor_desktop.Views
{
    public partial class BreakSelectionWindow : Window
    {
        public BreakType SelectedBreakType { get; private set; }
        public string Notes { get; private set; }
        public bool BreakSelected { get; private set; }

        public BreakSelectionWindow()
        {
            try
            {
                InitializeComponent();

                // Set window properties for better compatibility
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                this.Topmost = true; // Make sure it appears on top

                // Log for debugging (will write to file in published version)
                LogDebug("BreakSelectionWindow initialized successfully");
            }
            catch (Exception ex)
            {
                LogError("BreakSelectionWindow constructor error", ex);
                throw;
            }
        }

        private void BreakType_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as System.Windows.Controls.Button;
                if (button?.Tag != null)
                {
                    SelectedBreakType = (BreakType)System.Enum.Parse(typeof(BreakType), button.Tag.ToString());
                    Notes = NotesBox.Text;
                    BreakSelected = true;

                    LogDebug($"Break type selected: {SelectedBreakType}");

                    DialogResult = true;
                    Close();
                }
            }
            catch (Exception ex)
            {
                LogError("BreakType_Click error", ex);
                MessageBox.Show($"Error selecting break: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            BreakSelected = false;
            DialogResult = false;
            Close();
        }

        private void LogDebug(string message)
        {
            try
            {
                var logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WorkPulse", "debug.log");
                var dir = System.IO.Path.GetDirectoryName(logPath);
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                System.IO.File.AppendAllText(logPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [DEBUG] {message}\n");
            }
            catch { /* Ignore logging errors */ }
        }

        private void LogError(string context, Exception ex)
        {
            try
            {
                var logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WorkPulse", "error.log");
                var dir = System.IO.Path.GetDirectoryName(logPath);
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                System.IO.File.AppendAllText(logPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [ERROR] {context}: {ex.Message}\n{ex.StackTrace}\n");
            }
            catch { /* Ignore logging errors */ }
        }
    }
}