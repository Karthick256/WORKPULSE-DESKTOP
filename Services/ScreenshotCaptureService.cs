using monitor_desktop.Models.ActivityMonitoring;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;


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

            // Poll every 10 seconds for screenshot requests
            _pollingTimer = new System.Timers.Timer(10000);
            _pollingTimer.Elapsed += async (sender, e) => await PollForScreenshotRequests();
            _pollingTimer.AutoReset = true;
            _pollingTimer.Start();

            AddDebugLog("Screenshot polling started");
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
            AddDebugLog("Screenshot polling stopped");
        }

        private async Task PollForScreenshotRequests()
        {
            if (!_isPolling || _currentSessionId == 0) return;

            try
            {
                var response = await _trackingService.GetPendingScreenshotRequests(_currentSessionId);

                // Debug logging
                AddDebugLog($"Poll response status: {response.Status}");
                AddDebugLog($"Poll response message: {response.Message}");

                if (response.Status == 200 && response.Data != null && response.Data.Count > 0)
                {
                    AddDebugLog($"Found {response.Data.Count} pending screenshot requests");

                    foreach (var request in response.Data)
                    {
                        AddDebugLog($"📸 Processing screenshot request: ID={request.RequestId}, Status={request.Status}, Reason={request.RequestReason}");
                        await ProcessScreenshotRequest(request);
                    }
                }
                else if (response.Status == 200)
                {
                    // No pending requests - this is normal
                    AddDebugLog($"No pending screenshot requests for session {_currentSessionId}");
                }
                else
                {
                    AddDebugLog($"Error getting pending requests: {response.Message}");
                }
            }
            catch (Exception ex)
            {
                AddDebugLog($"Error polling screenshot requests: {ex.Message}");
                AddDebugLog($"Stack trace: {ex.StackTrace}");
            }
        }

        private async Task ProcessScreenshotRequest(ScreenshotResponseDto request)
        {
            try
            {
                AddDebugLog($"Capturing screenshot for request {request.RequestId}...");

                // Capture screenshot
                byte[] screenshotBytes = CaptureFullScreen();
                string base64Image = Convert.ToBase64String(screenshotBytes);

                AddDebugLog($"Screenshot captured: {screenshotBytes.Length / 1024} KB");

                // Prepare upload
                var uploadDto = new DesktopAgentScreenshotUploadDto
                {
                    RequestId = request.RequestId,
                    SessionId = _currentSessionId,
                    ImageBase64 = base64Image,
                    ImageFormat = "PNG",
                    Success = true
                };

                // Upload to server
                var uploadResponse = await _trackingService.UploadScreenshot(uploadDto);

                AddDebugLog($"Upload response status: {uploadResponse.Status}");
                AddDebugLog($"Upload response message: {uploadResponse.Message}");

                if (uploadResponse.Status == 200)
                {
                    AddDebugLog($"✓ Screenshot uploaded successfully for request {request.RequestId}");
                }
                else
                {
                    AddDebugLog($"✗ Failed to upload screenshot: {uploadResponse.Message}");
                }
            }
            catch (Exception ex)
            {
                AddDebugLog($"✗ Error processing screenshot request {request.RequestId}: {ex.Message}");
                AddDebugLog($"Stack trace: {ex.StackTrace}");

                // Report failure
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
                // Get the bounds of all screens
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
                AddDebugLog($"Failed to capture screen: {ex.Message}");
                throw;
            }
        }

        private Rectangle GetScreenBounds()
        {
            // For multiple monitors, we need to get the virtual screen bounds
            // This is a simplified version - returns primary screen
            var screenWidth = (int)SystemParameters.PrimaryScreenWidth;
            var screenHeight = (int)SystemParameters.PrimaryScreenHeight;

            return new Rectangle(0, 0, screenWidth, screenHeight);
        }

        private byte[] CaptureScreenUsingWPF()
        {
            try
            {
                // Get the primary screen bounds
                var screenWidth = SystemParameters.PrimaryScreenWidth;
                var screenHeight = SystemParameters.PrimaryScreenHeight;

                // Create a render bitmap
                var renderBitmap = new RenderTargetBitmap(
                    (int)screenWidth,
                    (int)screenHeight,
                    96, 96,
                    PixelFormats.Pbgra32);

                // Create a drawing visual
                var drawingVisual = new DrawingVisual();
                using (var drawingContext = drawingVisual.RenderOpen())
                {
                    // Draw the screen content
                    drawingContext.DrawRectangle(
                        new VisualBrush(Application.Current.MainWindow),
                        null,
                        new Rect(0, 0, screenWidth, screenHeight));
                }

                // Render the visual
                renderBitmap.Render(drawingVisual);

                // Encode as PNG
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderBitmap));

                using (var stream = new MemoryStream())
                {
                    encoder.Save(stream);
                    return stream.ToArray();
                }
            }
            catch (Exception ex)
            {
                AddDebugLog($"Failed to capture screen using WPF: {ex.Message}");
                // Fallback to alternative method
                return CaptureScreenAlternative();
            }
        }

        private byte[] CaptureScreenAlternative()
        {
            try
            {
                // Alternative: Capture using Windows API via direct screen capture
                // This is a simplified version - for full multi-monitor support, 
                // you would need P/Invoke to GetDC/CreateCompatibleDC

                var screenWidth = (int)SystemParameters.PrimaryScreenWidth;
                var screenHeight = (int)SystemParameters.PrimaryScreenHeight;

                var renderBitmap = new RenderTargetBitmap(screenWidth, screenHeight, 96, 96, PixelFormats.Pbgra32);

                // Create a visual brush of the entire desktop
                var desktopVisual = new DrawingVisual();
                using (var context = desktopVisual.RenderOpen())
                {
                    var desktopBrush = new VisualBrush(Application.Current.MainWindow?.Content as UIElement)
                    {
                        Viewbox = new Rect(0, 0, screenWidth, screenHeight),
                        ViewboxUnits = BrushMappingMode.Absolute,
                        Viewport = new Rect(0, 0, screenWidth, screenHeight),
                        ViewportUnits = BrushMappingMode.Absolute,
                        Stretch = Stretch.None
                    };
                    context.DrawRectangle(desktopBrush, null, new Rect(0, 0, screenWidth, screenHeight));
                }

                renderBitmap.Render(desktopVisual);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderBitmap));

                using (var stream = new MemoryStream())
                {
                    encoder.Save(stream);
                    return stream.ToArray();
                }
            }
            catch (Exception ex)
            {
                AddDebugLog($"Alternative capture failed: {ex.Message}");
                throw new InvalidOperationException("Unable to capture screenshot", ex);
            }
        }

        private void AddDebugLog(string message)
        {
            var logEntry = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Debug.WriteLine(logEntry);
            StatusChanged?.Invoke(this, logEntry);
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