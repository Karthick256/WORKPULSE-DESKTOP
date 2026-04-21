
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
            var response = await _apiClient.PostAsync<JwtResponse>(ApiConfig.AuthSignIn, request);

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

        public void Logout()
        {
            _tokenManager.ClearToken();
            _apiClient.ClearAuthToken();
        }

        public bool IsAuthenticated => _tokenManager.IsAuthenticated;
    }
}