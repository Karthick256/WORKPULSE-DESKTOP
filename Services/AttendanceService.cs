using monitor_desktop.Models;
using monitor_desktop.Models.ActivityMonitoring;
using monitor_desktop.Models.Enums;
using System.Diagnostics;

namespace monitor_desktop.Services
{
    public class AttendanceService
    {
        private readonly ApiClient _apiClient;

        public AttendanceService(ApiClient apiClient)
        {
            _apiClient = apiClient;
        }

        public async Task<ApiResponse<AttendanceSessionResponse>> CheckIn(CheckInRequest request)
        {
            try
            {
                return await _apiClient.PostAsync<AttendanceSessionResponse>(ApiConfig.AttendanceCheckIn, request);
            }
            catch (Exception ex)
            {
                return new ApiResponse<AttendanceSessionResponse>
                {
                    Status = 500,
                    Message = $"Check-in failed: {ex.Message}",
                    Data = null
                };
            }
        }

        // Updated CheckOut with time parameter
        public async Task<ApiResponse<AttendanceSessionResponse>> CheckOut(long sessionId, DateTime checkOutTime)
        {
            try
            {
                // With milliseconds: 2026-04-25T13:35:42.123
                var formattedTime = checkOutTime.ToString("yyyy-MM-ddTHH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);

                var url = $"{ApiConfig.AttendanceCheckOut}/{sessionId}?checkOutTime={Uri.EscapeDataString(formattedTime)}";

                Debug.WriteLine($"Checkout URL: {url}");

                return await _apiClient.PostAsync<AttendanceSessionResponse>(url, null);
            }
            catch (Exception ex)
            {
                return new ApiResponse<AttendanceSessionResponse>
                {
                    Status = 500,
                    Message = $"Check-out failed: {ex.Message}",
                    Data = null
                };
            }
        }

        public async Task<ApiResponse<AttendanceSessionResponse>> GetActiveSession()
        {
            try
            {
                return await _apiClient.GetAsync<AttendanceSessionResponse>(ApiConfig.AttendanceActiveSession);
            }
            catch (Exception ex)
            {
                return new ApiResponse<AttendanceSessionResponse>
                {
                    Status = 500,
                    Message = $"Failed to get active session: {ex.Message}",
                    Data = null
                };
            }
        }

        public async Task<bool> HasActiveSession()
        {
            try
            {
                var response = await GetActiveSession();
                return response.Status == 200 && response.Data != null && response.Data.SessionStatus == SessionStatus.ACTIVE;
            }
            catch
            {
                return false;
            }
        }

        public async Task<ApiResponse<AttendanceSessionResponse>> AutoCheckIn()
        {
            var request = new CheckInRequest
            {
                WorkstationName = Environment.MachineName,
                IpAddress = GetLocalIpAddress(),
                OsInfo = GetOperatingSystemInfo(),
                MacAddress = GetMacAddress(),
                CheckInTime = DateTime.Now  // Client provides current time
            };
            return await CheckIn(request);
        }

        private string GetLocalIpAddress()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return ip.ToString();
                }
            }
            catch { }
            return "Unknown";
        }

        private string GetOperatingSystemInfo()
        {
            try
            {
                var os = Environment.OSVersion;
                return $"{os.Platform} {os.VersionString}";
            }
            catch { return "Unknown"; }
        }

        private string GetMacAddress()
        {
            try
            {
                var networkInterfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in networkInterfaces)
                {
                    if (ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                        ni.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    {
                        return ni.GetPhysicalAddress().ToString();
                    }
                }
            }
            catch { }
            return "Unknown";
        }
    }
}