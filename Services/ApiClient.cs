using monitor_desktop.Converters;
using monitor_desktop.Models;
using monitor_desktop.Models.AuthManagement;
using System.IO;
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

        public ApiClient()
        {
            _httpClient = new HttpClient();
            _tokenManager = new TokenManager();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.BaseAddress = new Uri(ApiConfig.ApiBaseUrl + "/");

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters =
                    {
                       new CustomDateTimeConverter(),
                       new CustomNullableDateTimeConverter()
                    }
            };
            var token = _tokenManager.LoadToken();
            if (token != null)
            {
                SetAuthToken(token.Token);
            }
        }

        public void SetAuthToken(string token)
        {
            if (!string.IsNullOrEmpty(token))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }
            else
            {
                _httpClient.DefaultRequestHeaders.Authorization = null;
            }
        }

        public void ClearAuthToken()
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
            _tokenManager.ClearToken();
        }

        public TokenStorage GetTokenInfo() => _tokenManager.CurrentToken;
        public bool IsAuthenticated => _tokenManager.IsAuthenticated;

        private async Task<HttpResponseMessage> SendWithAuthCheckAsync(Func<Task<HttpResponseMessage>> action)
        {
            var response = await action();

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                ClearAuthToken();
            }

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

        public async Task<ApiResponse<T>> PutAsync<T>(string url, object data)
        {
            try
            {
                var json = JsonSerializer.Serialize(data, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await SendWithAuthCheckAsync(() => _httpClient.PutAsync(url, content));
                return await HandleResponse<T>(response);
            }
            catch (Exception ex)
            {
                return new ApiResponse<T> { Status = 500, Message = $"Network error: {ex.Message}" };
            }
        }

        public async Task<ApiResponse<T>> DeleteAsync<T>(string url)
        {
            try
            {
                var response = await SendWithAuthCheckAsync(() => _httpClient.DeleteAsync(url));
                return await HandleResponse<T>(response);
            }
            catch (Exception ex)
            {
                return new ApiResponse<T> { Status = 500, Message = $"Delete error: {ex.Message}" };
            }
        }

        public async Task<byte[]> GetByteArrayAsync(string url)
        {
            try
            {
                var response = await SendWithAuthCheckAsync(() => _httpClient.GetAsync(url));
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsByteArrayAsync();
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting byte array: {ex.Message}");
                return null;
            }
        }

        public async Task<ApiResponse<T>> UploadFileAsync<T>(string url, string filePath)
        {
            try
            {
                using (var formData = new MultipartFormDataContent())
                {
                    var fileBytes = File.ReadAllBytes(filePath);
                    var fileContent = new ByteArrayContent(fileBytes);
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(filePath));
                    formData.Add(fileContent, "file", Path.GetFileName(filePath));

                    var response = await SendWithAuthCheckAsync(() => _httpClient.PostAsync(url, formData));
                    return await HandleResponse<T>(response);
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<T> { Status = 500, Message = $"Upload error: {ex.Message}" };
            }
        }

        public async Task<ApiResponse<T>> UpdateFileAsync<T>(string url, string filePath)
        {
            try
            {
                using (var formData = new MultipartFormDataContent())
                {
                    var fileBytes = File.ReadAllBytes(filePath);
                    var fileContent = new ByteArrayContent(fileBytes);
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(filePath));
                    formData.Add(fileContent, "file", Path.GetFileName(filePath));

                    var response = await SendWithAuthCheckAsync(() => _httpClient.PutAsync(url, formData));
                    return await HandleResponse<T>(response);
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<T> { Status = 500, Message = $"Update error: {ex.Message}" };
            }
        }

        private async Task<ApiResponse<T>> HandleResponse<T>(HttpResponseMessage response)
        {
            var content = await response.Content.ReadAsStringAsync();

            try
            {
                if (!string.IsNullOrEmpty(content))
                {
                    var backendResponse = JsonSerializer.Deserialize<BackendApiResponse<T>>(content, _jsonOptions);

                    if (backendResponse != null && backendResponse.Data != null)
                    {
                        return new ApiResponse<T>
                        {
                            Status = backendResponse.Status,
                            Message = backendResponse.Message ?? (response.IsSuccessStatusCode ? "Success" : "Error"),
                            Data = backendResponse.Data
                        };
                    }

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

                    return new ApiResponse<T>
                    {
                        Status = (int)response.StatusCode,
                        Message = $"Unable to parse response: {content.Substring(0, Math.Min(100, content.Length))}",
                        Data = default
                    };
                }
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"JSON Deserialization Error: {ex.Message}");
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

        private string GetContentType(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLower();
            return extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                _ => "application/octet-stream"
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