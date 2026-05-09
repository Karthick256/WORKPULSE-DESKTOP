using monitor_desktop.Models.ActivityMonitoring;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;

namespace monitor_desktop.Services
{
    public class ScreenshotCaptureService : IDisposable
    {
        private readonly ActivityTrackingService _trackingService;
        private readonly TokenManager _tokenManager;
        private System.Timers.Timer _pollingTimer;
        private long _currentSessionId;
        private bool _isPolling;
        private bool _isDisposed;
        private readonly object _lockObject = new object();

        public event EventHandler<string> StatusChanged;

        public ScreenshotCaptureService(ActivityTrackingService trackingService, TokenManager tokenManager)
        {
            _trackingService = trackingService;
            _tokenManager = tokenManager;
        }

        public void StartPolling(long sessionId)
        {
            lock (_lockObject)
            {
                if (_isPolling) StopPolling();

                _currentSessionId = sessionId;
                _isPolling = true;

                _pollingTimer = new System.Timers.Timer(5000); // Poll every 5 seconds
                _pollingTimer.Elapsed += async (sender, e) => await PollForScreenshotRequests();
                _pollingTimer.AutoReset = true;
                _pollingTimer.Start();

                Debug.WriteLine($"[SCREENSHOT] Started polling for session {sessionId}");
            }
        }

        public void StopPolling()
        {
            lock (_lockObject)
            {
                if (_pollingTimer != null)
                {
                    _pollingTimer.Stop();
                    _pollingTimer.Dispose();
                    _pollingTimer = null;
                }
                _isPolling = false;
                Debug.WriteLine("[SCREENSHOT] Stopped polling");
            }
        }

        private async Task PollForScreenshotRequests()
        {
            lock (_lockObject)
            {
                if (!_isPolling || _currentSessionId == 0) return;
            }

            try
            {
                var response = await _trackingService.GetPendingScreenshotRequests(_currentSessionId);

                if (response.Status == 200 && response.Data != null && response.Data.Count > 0)
                {
                    Debug.WriteLine($"[SCREENSHOT] Found {response.Data.Count} pending screenshot requests");

                    foreach (var request in response.Data)
                    {
                        // Only process PENDING requests
                        if (request.Status == "PENDING")
                        {
                            await ProcessScreenshotRequest(request);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SCREENSHOT] Error polling requests: {ex.Message}");
            }
        }

        private async Task ProcessScreenshotRequest(ScreenshotResponseDto request)
        {
            Debug.WriteLine($"[SCREENSHOT] Processing request {request.RequestId} for session {request.SessionId}");

            try
            {
                // Capture full screen
                byte[] screenshotBytes = CaptureFullScreen();
                string base64Image = Convert.ToBase64String(screenshotBytes);

                var imageSizeKB = screenshotBytes.Length / 1024;
                Debug.WriteLine($"[SCREENSHOT] Captured screenshot for request {request.RequestId}, size: {imageSizeKB}KB");

                var uploadDto = new DesktopAgentScreenshotUploadDto
                {
                    RequestId = request.RequestId,
                    SessionId = _currentSessionId,
                    ImageBase64 = base64Image,
                    ImageFormat = "PNG",
                    Success = true
                };

                var response = await _trackingService.UploadScreenshot(uploadDto);

                if (response.Status == 200 || response.Status == 201)
                {
                    Debug.WriteLine($"[SCREENSHOT] Successfully uploaded screenshot for request {request.RequestId}");
                    StatusChanged?.Invoke(this, $"Screenshot {request.RequestId} uploaded successfully");
                }
                else
                {
                    Debug.WriteLine($"[SCREENSHOT] Failed to upload screenshot: {response.Message}");
                    StatusChanged?.Invoke(this, $"Failed to upload screenshot for request {request.RequestId}: {response.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SCREENSHOT] Error processing request {request.RequestId}: {ex.Message}");
                StatusChanged?.Invoke(this, $"Error processing screenshot request: {ex.Message}");

                // Send failure response
                try
                {
                    var uploadDto = new DesktopAgentScreenshotUploadDto
                    {
                        RequestId = request.RequestId,
                        SessionId = _currentSessionId,
                        Success = false,
                        ErrorMessage = ex.Message
                    };

                    await _trackingService.UploadScreenshot(uploadDto);
                }
                catch (Exception uploadEx)
                {
                    Debug.WriteLine($"[SCREENSHOT] Failed to send error response: {uploadEx.Message}");
                }
            }
        }

        private byte[] CaptureFullScreen()
        {
            try
            {
                var bounds = GetScreenBounds();

                using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
                {
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);
                    }

                    using (var ms = new MemoryStream())
                    {
                        bitmap.Save(ms, ImageFormat.Png);
                        return ms.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SCREENSHOT] Failed to capture screen: {ex.Message}");
                throw;
            }
        }

        private Rectangle GetScreenBounds()
        {
            var screenWidth = (int)SystemParameters.PrimaryScreenWidth;
            var screenHeight = (int)SystemParameters.PrimaryScreenHeight;
            return new Rectangle(0, 0, screenWidth, screenHeight);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            StopPolling();
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }
}