using monitor_desktop.Models;
using monitor_desktop.Models.ProfileManagement;

namespace monitor_desktop.Services
{
    public class ProfileService
    {
        private readonly ApiClient _apiClient;

        public ProfileService(ApiClient apiClient)
        {
            _apiClient = apiClient;
        }

        public async Task<ApiResponse<UserProfileDTO>> GetMyProfile()
        {
            return await _apiClient.GetAsync<UserProfileDTO>("profiles/get-my-profile");
        }

        public async Task<ApiResponse<UserProfileDTO>> UpdateMyProfile(UpdateProfileRequest profileRequest)
        {
            return await _apiClient.PutAsync<UserProfileDTO>("profiles/update-my-profile", profileRequest);
        }

        public async Task<byte[]> GetProfileImage()
        {
            return await _apiClient.GetByteArrayAsync("profiles/get-profile-image");
        }

        public async Task<ApiResponse<ProfileImageDTO>> UploadProfileImage(string filePath)
        {
            return await _apiClient.UploadFileAsync<ProfileImageDTO>("profiles/upload-profile-image", filePath);
        }

        public async Task<ApiResponse<ProfileImageDTO>> UpdateProfileImage(string filePath)
        {
            return await _apiClient.UpdateFileAsync<ProfileImageDTO>("profiles/update-profile-image", filePath);
        }

        public async Task<ApiResponse<object>> DeleteProfileImage()
        {
            return await _apiClient.DeleteAsync<object>("profiles/delete-profile-image");
        }
    }
}