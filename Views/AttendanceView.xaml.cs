using System.Windows.Controls;
using monitor_desktop.ViewModels;

namespace monitor_desktop.Views
{
    public partial class AttendanceView : UserControl
    {
        private readonly AttendanceViewModel _viewModel;

        public AttendanceView()
        {
            InitializeComponent();
            _viewModel = new AttendanceViewModel();
            DataContext = _viewModel;

            this.Unloaded += (s, e) => _viewModel.Dispose();
        }
    }
}