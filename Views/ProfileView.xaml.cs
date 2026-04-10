using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using monitor_desktop.ViewModels;

namespace monitor_desktop.Views
{
    public partial class ProfileView : UserControl
    {
        private readonly ProfileViewModel _viewModel;
        public ProfileView()
        {
            InitializeComponent();
            _viewModel = new ProfileViewModel();
            DataContext = _viewModel;
        }

        private async void EditProfile_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.EditMode();
        }

        private async void CancelEdit_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.CancelEdit();
        }

        private async void SaveProfile_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.UpdateProfile();
        }

        private async void UploadPhoto_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Profile Image",
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.gif;*.bmp",
                Multiselect = false
            };
            if (dialog.ShowDialog() == true)
            {
                await _viewModel.UploadProfileImage(dialog.FileName);
            }
        }

        private async void UpdatePhoto_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Profile Image",
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.gif;*.bmp",
                Multiselect = false
            };
            if (dialog.ShowDialog() == true)
            {
                await _viewModel.UpdateProfileImage(dialog.FileName);
            }
        }

        private async void DeletePhoto_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to delete your profile image?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                await _viewModel.DeleteProfileImage();
            }
        }
    }
}