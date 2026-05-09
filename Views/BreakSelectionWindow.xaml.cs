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
            InitializeComponent();
        }

        private void BreakType_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            if (button?.Tag != null)
            {
                SelectedBreakType = (BreakType)System.Enum.Parse(typeof(BreakType), button.Tag.ToString());
                Notes = NotesBox.Text;
                BreakSelected = true;
                DialogResult = true;
                Close();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            BreakSelected = false;
            DialogResult = false;
            Close();
        }
    }
}