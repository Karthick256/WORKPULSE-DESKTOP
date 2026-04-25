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

        public event EventHandler<string> StatusChanged;

        public ScreenshotCaptureService(ActivityTrackingService trackingService, TokenManager tokenManager)
        {
            _trackingService = trackingService;
            _tokenManager = tokenManager;
        }

        public void StartPolling(long sessionId)
        {
            if (_isPolling) StopPolling();

            _currentSessionId = sessionId;
            _isPolling = true;

            _pollingTimer = new System.Timers.Timer(10000);
            _pollingTimer.Elapsed += async (sender, e) => await PollForScreenshotRequests();
            _pollingTimer.AutoReset = true;
            _pollingTimer.Start();
        }

        public void StopPolling()
        {
            if (_pollingTimer != null)
            {
                _pollingTimer.Stop();
                _pollingTimer.Dispose();
                _pollingTimer = null;
            }
            _isPolling = false;
        }

        private async Task PollForScreenshotRequests()
        {
            if (!_isPolling || _currentSessionId == 0) return;

            try
            {
                var response = await _trackingService.GetPendingScreenshotRequests(_currentSessionId);

                if (response.Status == 200 && response.Data != null && response.Data.Count > 0)
                {
                    foreach (var request in response.Data)
                    {
                        await ProcessScreenshotRequest(request);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error polling screenshot requests: {ex.Message}");
            }
        }

        private async Task ProcessScreenshotRequest(ScreenshotResponseDto request)
        {
            try
            {
                byte[] screenshotBytes = CaptureFullScreen();
                string base64Image = Convert.ToBase64String(screenshotBytes);

                var uploadDto = new DesktopAgentScreenshotUploadDto
                {
                    RequestId = request.RequestId,
                    SessionId = _currentSessionId,
                    ImageBase64 = base64Image,
                    ImageFormat = "PNG",
                    Success = true
                };

                await _trackingService.UploadScreenshot(uploadDto);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing screenshot request {request.RequestId}: {ex.Message}");

                var uploadDto = new DesktopAgentScreenshotUploadDto
                {
                    RequestId = request.RequestId,
                    SessionId = _currentSessionId,
                    Success = false,
                    ErrorMessage = ex.Message
                };

                await _trackingService.UploadScreenshot(uploadDto);
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
                Debug.WriteLine($"Failed to capture screen: {ex.Message}");
                throw;
            }
        }

        private Rectangle GetScreenBounds()
        {
            var screenWidth = (int)SystemParameters.PrimaryScreenWidth;
            var screenHeight = (int)SystemParameters.PrimaryScreenHeight;
            return new Rectangle(0, 0, screenWidth, screenHeight);
        }

        private void AddDebugLog(string message)
        {
            StatusChanged?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {message}");
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