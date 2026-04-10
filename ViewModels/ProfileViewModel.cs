using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.IO;
using monitor_desktop.Models.AuthManagement;
using monitor_desktop.Models.ProfileManagement;
using monitor_desktop.Services;

namespace monitor_desktop.ViewModels
{
    public class ProfileViewModel : INotifyPropertyChanged
    {
        private readonly ApiClient _apiClient;
        private readonly ProfileService _profileService;

        private UserProfileDTO _profile;
        private bool _isEditing;
        private bool _isLoading;
        private string _errorMessage;
        private bool _hasError;
        private string _successMessage;
        private bool _hasSuccess;
        private BitmapImage _profileImage;
        private UpdateProfileRequest _editProfile;

        public UserProfileDTO Profile
        {
            get => _profile;
            set { _profile = value; OnPropertyChanged(); }
        }

        public UpdateProfileRequest EditProfile
        {
            get => _editProfile;
            set { _editProfile = value; OnPropertyChanged(); }
        }

        public bool IsEditing
        {
            get => _isEditing;
            set { _isEditing = value; OnPropertyChanged(); }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        public bool HasError
        {
            get => _hasError;
            set { _hasError = value; OnPropertyChanged(); }
        }

        public string SuccessMessage
        {
            get => _successMessage;
            set { _successMessage = value; OnPropertyChanged(); }
        }

        public bool HasSuccess
        {
            get => _hasSuccess;
            set { _hasSuccess = value; OnPropertyChanged(); }
        }

        public BitmapImage ProfileImage
        {
            get => _profileImage;
            set { _profileImage = value; OnPropertyChanged(); }
        }

        public ProfileViewModel()
        {
            _apiClient = new ApiClient();
            _profileService = new ProfileService(_apiClient);
            LoadProfile();
        }

        public async Task LoadProfile()
        {
            IsLoading = true;
            ClearMessages();

            try
            {
                var response = await _profileService.GetMyProfile();

                if (response.Status == 200 && response.Data != null)
                {
                    Profile = response.Data;
                    InitializeEditProfile();
                    await LoadProfileImage();
                }
                else
                {
                    ErrorMessage = response.Message ?? "Failed to load profile";
                    HasError = true;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error loading profile: {ex.Message}";
                HasError = true;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void InitializeEditProfile()
        {
            EditProfile = new UpdateProfileRequest
            {
                FirstName = Profile?.FirstName,
                LastName = Profile?.LastName,
                Username = Profile?.Username,
                Email = Profile?.Email,
                Phone = Profile?.Phone,
                Address = Profile?.Address != null ? new AddressDTO
                {
                    Street = Profile.Address.Street,
                    City = Profile.Address.City,
                    State = Profile.Address.State,
                    PostalCode = Profile.Address.PostalCode,
                    Country = Profile.Address.Country
                } : new AddressDTO()
            };
        }

        private async Task LoadProfileImage()
        {
            try
            {
                var imageBytes = await _profileService.GetProfileImage();
                if (imageBytes != null && imageBytes.Length > 0)
                {
                    using (var stream = new MemoryStream(imageBytes))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                        ProfileImage = bitmap;
                    }
                }
            }
            catch (Exception ex)
            {
                // Profile image might not exist - that's fine
                System.Diagnostics.Debug.WriteLine($"No profile image: {ex.Message}");
            }
        }

        public async Task<bool> UpdateProfile()
        {
            if (!ValidateProfile())
                return false;

            IsLoading = true;
            ClearMessages();

            try
            {
                var response = await _profileService.UpdateMyProfile(EditProfile);

                if (response.Status == 200 && response.Data != null)
                {
                    Profile = response.Data;
                    SuccessMessage = "Profile updated successfully!";
                    HasSuccess = true;
                    IsEditing = false;
                    return true;
                }
                else
                {
                    ErrorMessage = response.Message ?? "Failed to update profile";
                    HasError = true;
                    return false;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error updating profile: {ex.Message}";
                HasError = true;
                return false;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private bool ValidateProfile()
        {
            if (string.IsNullOrWhiteSpace(EditProfile.FirstName))
            {
                ErrorMessage = "First name is required";
                HasError = true;
                return false;
            }

            if (string.IsNullOrWhiteSpace(EditProfile.LastName))
            {
                ErrorMessage = "Last name is required";
                HasError = true;
                return false;
            }

            if (string.IsNullOrWhiteSpace(EditProfile.Email))
            {
                ErrorMessage = "Email is required";
                HasError = true;
                return false;
            }

            if (!IsValidEmail(EditProfile.Email))
            {
                ErrorMessage = "Please enter a valid email address";
                HasError = true;
                return false;
            }

            if (string.IsNullOrWhiteSpace(EditProfile.Phone))
            {
                ErrorMessage = "Phone number is required";
                HasError = true;
                return false;
            }

            return true;
        }

        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> UploadProfileImage(string filePath)
        {
            IsLoading = true;
            ClearMessages();

            try
            {
                var response = await _profileService.UploadProfileImage(filePath);

                if (response.Status == 200 && response.Data != null)
                {
                    await LoadProfileImage();
                    SuccessMessage = "Profile image uploaded successfully!";
                    HasSuccess = true;
                    return true;
                }
                else
                {
                    ErrorMessage = response.Message ?? "Failed to upload image";
                    HasError = true;
                    return false;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error uploading image: {ex.Message}";
                HasError = true;
                return false;
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task<bool> UpdateProfileImage(string filePath)
        {
            IsLoading = true;
            ClearMessages();

            try
            {
                var response = await _profileService.UpdateProfileImage(filePath);

                if (response.Status == 200 && response.Data != null)
                {
                    await LoadProfileImage();
                    SuccessMessage = "Profile image updated successfully!";
                    HasSuccess = true;
                    return true;
                }
                else
                {
                    ErrorMessage = response.Message ?? "Failed to update image";
                    HasError = true;
                    return false;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error updating image: {ex.Message}";
                HasError = true;
                return false;
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task<bool> DeleteProfileImage()
        {
            IsLoading = true;
            ClearMessages();

            try
            {
                var response = await _profileService.DeleteProfileImage();

                if (response.Status == 200)
                {
                    ProfileImage = null;
                    SuccessMessage = "Profile image deleted successfully!";
                    HasSuccess = true;
                    return true;
                }
                else
                {
                    ErrorMessage = response.Message ?? "Failed to delete image";
                    HasError = true;
                    return false;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error deleting image: {ex.Message}";
                HasError = true;
                return false;
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void EditMode()
        {
            InitializeEditProfile();
            IsEditing = true;
            ClearMessages();
        }

        public void CancelEdit()
        {
            IsEditing = false;
            ClearMessages();
        }

        private void ClearMessages()
        {
            HasError = false;
            ErrorMessage = string.Empty;
            HasSuccess = false;
            SuccessMessage = string.Empty;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}