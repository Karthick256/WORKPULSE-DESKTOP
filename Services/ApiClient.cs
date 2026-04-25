
using monitor_desktop.Converters;
using monitor_desktop.Models;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace monitor_desktop.Services
{
    public class ApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly TokenManager _tokenManager;
        private readonly JsonSerializerOptions _jsonOptions;

        public ApiClient(TokenManager tokenManager = null)
        {
            _tokenManager = tokenManager ?? new TokenManager();
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.BaseAddress = new Uri(ApiConfig.ApiBaseUrl + "/");

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new CustomDateTimeConverter(), new CustomNullableDateTimeConverter() }
            };

            var token = _tokenManager.LoadToken();
            if (token != null) SetAuthToken(token.Token);
        }

        public void SetAuthToken(string token)
        {
            if (!string.IsNullOrEmpty(token))
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            else
                _httpClient.DefaultRequestHeaders.Authorization = null;
        }

        public void ClearAuthToken()
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
            _tokenManager.ClearToken();
        }

        public bool IsAuthenticated => _tokenManager.IsAuthenticated;

        private async Task<HttpResponseMessage> SendWithAuthCheckAsync(Func<Task<HttpResponseMessage>> action)
        {
            var response = await action();
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) ClearAuthToken();
            return response;
        }

        public async Task<ApiResponse<T>> GetAsync<T>(string url)
        {
            try
            {
                var response = await SendWithAuthCheckAsync(() => _httpClient.GetAsync(url));
                return await HandleResponse<T>(response);
            }
            catch (Exception ex)
            {
                return new ApiResponse<T> { Status = 500, Message = $"Network error: {ex.Message}" };
            }
        }

        public async Task<ApiResponse<T>> PostAsync<T>(string url, object data)
        {
            try
            {
                HttpResponseMessage response;
                if (data != null)
                {
                    var json = JsonSerializer.Serialize(data, _jsonOptions);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    response = await SendWithAuthCheckAsync(() => _httpClient.PostAsync(url, content));
                }
                else
                {
                    response = await SendWithAuthCheckAsync(() => _httpClient.PostAsync(url, null));
                }
                return await HandleResponse<T>(response);
            }
            catch (Exception ex)
            {
                return new ApiResponse<T> { Status = 500, Message = $"Network error: {ex.Message}" };
            }
        }

        private async Task<ApiResponse<T>> HandleResponse<T>(HttpResponseMessage response)
        {
            var content = await response.Content.ReadAsStringAsync();

            // Log the raw response for debugging
            Debug.WriteLine($"Response Status: {response.StatusCode}");
            Debug.WriteLine($"Response Content: {content}");

            try
            {
                if (!string.IsNullOrEmpty(content))
                {
                    // Try to deserialize as ApiResponse<T>
                    var backendResponse = JsonSerializer.Deserialize<ApiResponse<T>>(content, _jsonOptions);
                    if (backendResponse != null)
                    {
                        return new ApiResponse<T>
                        {
                            Status = backendResponse.Status,
                            Message = backendResponse.Message ?? (response.IsSuccessStatusCode ? "Success" : "Error"),
                            Data = backendResponse.Data
                        };
                    }

                    // If that fails, try to deserialize as just T
                    var directData = JsonSerializer.Deserialize<T>(content, _jsonOptions);
                    if (directData != null)
                    {
                        return new ApiResponse<T>
                        {
                            Status = (int)response.StatusCode,
                            Message = response.IsSuccessStatusCode ? "Success" : "Error",
                            Data = directData
                        };
                    }

                    // If we can't deserialize to T, but have content, return error with content
                    if (!response.IsSuccessStatusCode)
                    {
                        return new ApiResponse<T>
                        {
                            Status = (int)response.StatusCode,
                            Message = $"Server error: {content}",
                            Data = default
                        };
                    }
                }
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"JSON Parse Error: {ex.Message}");
                Debug.WriteLine($"Raw content: {content}");

                return new ApiResponse<T>
                {
                    Status = (int)response.StatusCode,
                    Message = $"JSON parse error: {ex.Message}",
                    Data = default
                };
            }

            return new ApiResponse<T>
            {
                Status = (int)response.StatusCode,
                Message = response.IsSuccessStatusCode ? "Success" : $"HTTP Error: {response.StatusCode}",
                Data = default
            };
        }
    }

    public class BackendApiResponse<T>
    {
        public int Status { get; set; }
        public string Message { get; set; }
        public T Data { get; set; }
    }
}