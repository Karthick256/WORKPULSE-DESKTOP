using monitor_desktop.Models;
using monitor_desktop.Models.AuthManagement;

namespace monitor_desktop.Services
{
    public class AuthService
    {
        private readonly ApiClient _apiClient;
        private readonly TokenManager _tokenManager;

        public AuthService(ApiClient apiClient)
        {
            _apiClient = apiClient;
            _tokenManager = new TokenManager();
        }

        public async Task<ApiResponse<JwtResponse>> SignIn(LoginRequest request)
        {
            var response = await _apiClient.PostAsync<JwtResponse>("auth/signin", request);

            if (response.Status == 200 && response.Data != null)
            {
                var tokenStorage = new TokenStorage
                {
                    Token = response.Data.Token,
                    Type = response.Data.Type,
                    Roles = response.Data.Roles,
                    IssuedAt = response.Data.IssuedAt,
                    ExpiresAt = response.Data.ExpiresAt
                };
                _tokenManager.SaveToken(tokenStorage);
                _apiClient.SetAuthToken(response.Data.Token);
            }

            return response;
        }

        public async Task<ApiResponse<object>> ChangePassword(ChangePasswordRequest request)
        {
            return await _apiClient.PostAsync<object>("auth/change-password", request);
        }

        public async Task<ApiResponse<object>> ForgotPassword(ForgotPasswordRequest request)
        {
            return await _apiClient.PostAsync<object>("auth/forgot-password", request);
        }

        public async Task<ApiResponse<object>> ForgotUsername(ForgotUsernameRequest request)
        {
            return await _apiClient.PostAsync<object>("auth/forgot-username", request);
        }

        public void Logout()
        {
            _tokenManager.ClearToken();
            _apiClient.ClearAuthToken();
        }

        public TokenStorage GetCurrentToken() => _tokenManager.CurrentToken;

        public bool IsAuthenticated => _tokenManager.IsAuthenticated;
    }
}