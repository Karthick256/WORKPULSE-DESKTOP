using monitor_desktop.Models;
using monitor_desktop.Models.ActivityMonitoring;

namespace monitor_desktop.Services
{
    public class ActivityTrackingService
    {
        private readonly ApiClient _apiClient;

        public ActivityTrackingService(ApiClient apiClient)
        {
            _apiClient = apiClient;
        }

        public async Task<ApiResponse<VoidResponse>> SubmitBatch(ActivityBatchRequest batchRequest)
        {
            try
            {
                return await _apiClient.PostAsync<VoidResponse>(ApiConfig.TrackingBatch, batchRequest);
            }
            catch (Exception ex)
            {
                return new ApiResponse<VoidResponse>
                {
                    Status = 500,
                    Message = $"Failed to submit activity batch: {ex.Message}",
                    Data = null
                };
            }
        }

        public async Task<ApiResponse<VoidResponse>> SaveApplicationUsage(ApplicationUsageRequest request)
        {
            try
            {
                return await _apiClient.PostAsync<VoidResponse>(ApiConfig.TrackingApplication, request);
            }
            catch (Exception ex)
            {
                return new ApiResponse<VoidResponse>
                {
                    Status = 500,
                    Message = $"Failed to save application usage: {ex.Message}",
                    Data = null
                };
            }
        }

        public async Task<long?> SaveBrowserUsage(BrowserUsageRequest request)
        {
            try
            {
                var response = await _apiClient.PostAsync<IdResponse>(ApiConfig.TrackingBrowser, request);

                if (response.Status == 200 || response.Status == 201)
                {
                    if (response.Data != null && response.Data.Id > 0)
                    {
                        return response.Data.Id;
                    }
                    return -DateTime.Now.Ticks;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to save browser usage: {response.Message}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save browser usage: {ex.Message}");
                return null;
            }
        }

        public async Task<ApiResponse<VoidResponse>> SaveBrowserUrlVisit(BrowserUrlVisitRequest request)
        {
            try
            {
                return await _apiClient.PostAsync<VoidResponse>(ApiConfig.TrackingBrowserUrl, request);
            }
            catch (Exception ex)
            {
                return new ApiResponse<VoidResponse>
                {
                    Status = 500,
                    Message = $"Failed to save URL visit: {ex.Message}",
                    Data = null
                };
            }
        }

        public async Task<ApiResponse<VoidResponse>> SaveMouseActivity(MouseActivityRequest request)
        {
            try
            {
                return await _apiClient.PostAsync<VoidResponse>(ApiConfig.TrackingMouse, request);
            }
            catch (Exception ex)
            {
                return new ApiResponse<VoidResponse>
                {
                    Status = 500,
                    Message = $"Failed to save mouse activity: {ex.Message}",
                    Data = null
                };
            }
        }

        public async Task<ApiResponse<VoidResponse>> SaveKeyboardActivity(KeyboardActivityRequest request)
        {
            try
            {
                return await _apiClient.PostAsync<VoidResponse>(ApiConfig.TrackingKeyboard, request);
            }
            catch (Exception ex)
            {
                return new ApiResponse<VoidResponse>
                {
                    Status = 500,
                    Message = $"Failed to save keyboard activity: {ex.Message}",
                    Data = null
                };
            }
        }

        public async Task<ApiResponse<VoidResponse>> LogIdlePeriod(IdlePeriodRequest request)
        {
            try
            {
                return await _apiClient.PostAsync<VoidResponse>(ApiConfig.TrackingIdleStart, request);
            }
            catch (Exception ex)
            {
                return new ApiResponse<VoidResponse>
                {
                    Status = 500,
                    Message = $"Failed to log idle period: {ex.Message}",
                    Data = null
                };
            }
        }

        public async Task<ApiResponse<VoidResponse>> CloseIdlePeriod(long sessionId)
        {
            try
            {
                return await _apiClient.PostAsync<VoidResponse>($"{ApiConfig.TrackingIdleEnd}/{sessionId}", null);
            }
            catch (Exception ex)
            {
                return new ApiResponse<VoidResponse>
                {
                    Status = 500,
                    Message = $"Failed to close idle period: {ex.Message}",
                    Data = null
                };
            }
        }
    }

    public class VoidResponse
    {
      
    }
}