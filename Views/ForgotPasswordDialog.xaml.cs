using System.Windows;
using System.Windows.Input;
using monitor_desktop.ViewModels;

namespace monitor_desktop.Views
{
    public partial class ForgotPasswordDialog : Window
    {
        private readonly ForgotPasswordViewModel _viewModel;

        public ForgotPasswordDialog()
        {
            InitializeComponent();
            _viewModel = new ForgotPasswordViewModel();
            DataContext = _viewModel;
        }

        // ── Existing methods (unchanged) ─────────────────────────────────────

        private async void Reset_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.Email = EmailBox.Text;
            var success = await _viewModel.SendResetLinkAsync();

            if (success)
            {
                var timer = new System.Timers.Timer(3000);
                timer.Elapsed += (s, args) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    Dispatcher.Invoke(() => Close());
                };
                timer.Start();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ── New handlers for custom title bar ────────────────────────────────

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