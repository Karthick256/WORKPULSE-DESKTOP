using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using monitor_desktop.Models.ActivityMonitoring;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace monitor_desktop.Services
{
    public class LiveScreenStreamingService : IDisposable
    {
        private ClientWebSocket _webSocket;
        private Timer _frameTimer;
        private Timer _keepAliveTimer;
        private string _currentStreamId;
        private bool _isStreaming;
        private int _quality = 50;
        private int _targetFps = 5;
        private long _frameNumber;
        private readonly string _serverUrl;
        private readonly long _sessionId;
        private readonly TokenManager _tokenManager;
        private readonly ActivityTrackingService _trackingService;
        private bool _isConnected;
        private bool _isDisposed;
        private int _reconnectAttempts;
        private const int MAX_RECONNECT_ATTEMPTS = 5;
        private Timer _pollingTimer;
        private readonly object _lockObject = new object();
        private CancellationTokenSource _cts;
        private int _subscriptionId = 1;
        private readonly object _sendLock = new object();
        private bool _isStompConnected = false;
        private StringBuilder _partialMessage = new StringBuilder();
        private DateTime _lastFrameTime = DateTime.MinValue;
        private int _frameIntervalMs;

        // Performance tracking
        private int _framesSent = 0;
        private DateTime _lastStatsTime = DateTime.Now;

        public event EventHandler<string> StatusChanged;
        public event EventHandler<string> ErrorOccurred;
        public event EventHandler<bool> StreamingStatusChanged;

        public bool IsStreaming => _isStreaming;
        public string CurrentStreamId => _currentStreamId;

        public LiveScreenStreamingService(string serverUrl, long sessionId, TokenManager tokenManager, ActivityTrackingService trackingService)
        {
            _serverUrl = serverUrl;
            _sessionId = sessionId;
            _tokenManager = tokenManager;
            _trackingService = trackingService;
            _frameIntervalMs = 1000 / _targetFps;
        }

        public async Task ConnectAsync()
        {
            if (_isDisposed) return;

            lock (_lockObject)
            {
                if (_isConnected) return;
            }

            var wsUrl = BuildWebSocketUrl();
            Debug.WriteLine($"[LIVE-STREAM] Connecting to WebSocket: {wsUrl}");

            try
            {
                _cts = new CancellationTokenSource();
                _webSocket = new ClientWebSocket();

                // Add required WebSocket headers
                var token = _tokenManager.CurrentToken?.Token;
                if (!string.IsNullOrEmpty(token))
                {
                    _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {token}");
                    Debug.WriteLine("[LIVE-STREAM] Added Authorization header");
                }

                _webSocket.Options.SetRequestHeader("Origin", _serverUrl);
                _webSocket.Options.SetRequestHeader("User-Agent", "WorkPulse-DesktopAgent/1.0");
                _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

                // Connect
                await _webSocket.ConnectAsync(new Uri(wsUrl), _cts.Token);

                if (_webSocket.State == WebSocketState.Open)
                {
                    _isConnected = true;
                    _isStompConnected = false;
                    _reconnectAttempts = 0;
                    Debug.WriteLine("[LIVE-STREAM] WebSocket connected");
                    StatusChanged?.Invoke(this, "WebSocket connected");

                    // Start message receiver
                    _ = Task.Run(ReceiveMessagesAsync);

                    // Send STOMP CONNECT with correct frame format
                    await SendStompConnectAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LIVE-STREAM] Connection failed: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"Connection failed: {ex.Message}");
                await ScheduleReconnect();
            }
        }

        private string BuildWebSocketUrl()
        {
            var baseUrl = _serverUrl;

            // Convert http/https to ws/wss and clean up
            if (baseUrl.StartsWith("https://"))
                baseUrl = baseUrl.Replace("https://", "wss://");
            else if (baseUrl.StartsWith("http://"))
                baseUrl = baseUrl.Replace("http://", "ws://");
            else
                baseUrl = "ws://" + baseUrl;

            baseUrl = baseUrl.TrimEnd('/');

            // Use /ws endpoint
            var uri = new Uri(baseUrl);
            var wsUrl = $"{uri.Scheme}://{uri.Host}:{uri.Port}/ws";

            Debug.WriteLine($"[LIVE-STREAM] WebSocket URL: {wsUrl}");
            return wsUrl;
        }

        private async Task SendStompConnectAsync()
        {
            if (_webSocket?.State != WebSocketState.Open) return;

            var token = _tokenManager.CurrentToken?.Token;

            // Build correct STOMP CONNECT frame
            // IMPORTANT: Each line must end with \r\n (CRLF) according to STOMP spec
            var connectFrameBuilder = new StringBuilder();
            connectFrameBuilder.Append("CONNECT\r\n");
            connectFrameBuilder.Append("accept-version:1.2,1.1,1.0\r\n");
            connectFrameBuilder.Append("heart-beat:10000,10000\r\n");
            if (!string.IsNullOrEmpty(token))
            {
                connectFrameBuilder.Append($"Authorization:Bearer {token}\r\n");
            }
            connectFrameBuilder.Append("\r\n"); // Empty line to end headers
            connectFrameBuilder.Append("\0"); // NULL terminator

            var connectFrame = connectFrameBuilder.ToString();
            await SendRawFrameAsync(connectFrame);
            Debug.WriteLine("[LIVE-STREAM] STOMP CONNECT sent");
            Debug.WriteLine($"[LIVE-STREAM] CONNECT frame: {connectFrame.Replace("\r\n", "\\r\\n")}");

            // Start keep-alive after connection is established
            StartKeepAlive();
        }

        private async Task SendRawFrameAsync(string frame)
        {
            if (_webSocket?.State != WebSocketState.Open) return;

            try
            {
                var bytes = Encoding.UTF8.GetBytes(frame);
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    _cts.Token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LIVE-STREAM] Error sending frame: {ex.Message}");
            }
        }

        private async Task ReceiveMessagesAsync()
        {
            var buffer = new byte[65536];

            try
            {
                while (_webSocket?.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
                {
                    var result = await _webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        _cts.Token);

                    var messagePart = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    _partialMessage.Append(messagePart);

                    if (result.EndOfMessage)
                    {
                        var fullMessage = _partialMessage.ToString();
                        _partialMessage.Clear();

                        if (!string.IsNullOrEmpty(fullMessage))
                        {
                            Debug.WriteLine($"[LIVE-STREAM] Received: {fullMessage.Substring(0, Math.Min(200, fullMessage.Length))}");
                            await ProcessReceivedMessageAsync(fullMessage);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[LIVE-STREAM] Receive cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LIVE-STREAM] Receive error: {ex.Message}");
                if (!_isDisposed)
                {
                    _ = ScheduleReconnect();
                }
            }
        }

        private async Task ProcessReceivedMessageAsync(string message)
        {
            try
            {
                // Check for CONNECTED response
                if (message.StartsWith("CONNECTED"))
                {
                    Debug.WriteLine("[LIVE-STREAM] STOMP CONNECTED");
                    _isStompConnected = true;
                    await SubscribeToChannelsAsync();
                    await StartPollingPendingRequestsAsync();
                    StatusChanged?.Invoke(this, "STOMP connected");
                }
                else if (message.StartsWith("MESSAGE"))
                {
                    var body = ParseStompBody(message);
                    if (!string.IsNullOrEmpty(body))
                    {
                        Debug.WriteLine($"[LIVE-STREAM] Message body length: {body.Length}");
                        await ProcessMessageBodyAsync(body);
                    }
                }
                else if (message.StartsWith("RECEIPT"))
                {
                    Debug.WriteLine("[LIVE-STREAM] Receipt received");
                }
                else if (message.StartsWith("ERROR"))
                {
                    Debug.WriteLine($"[LIVE-STREAM] STOMP Error: {message}");
                    var errorBody = ParseStompBody(message);
                    if (!string.IsNullOrEmpty(errorBody))
                    {
                        ErrorOccurred?.Invoke(this, $"STOMP error: {errorBody}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LIVE-STREAM] Error processing message: {ex.Message}");
            }
        }

        private string ParseStompBody(string stompFrame)
        {
            try
            {
                // Find the double newline (CRLF CRLF or LF LF) that separates headers from body
                int headerEnd = stompFrame.IndexOf("\r\n\r\n");
                if (headerEnd == -1)
                {
                    headerEnd = stompFrame.IndexOf("\n\n");
                }
                if (headerEnd == -1) return null;

                int bodyStart = headerEnd + 2;
                if (stompFrame[headerEnd] == '\r')
                {
                    bodyStart = headerEnd + 4;
                }
                else
                {
                    bodyStart = headerEnd + 2;
                }

                if (bodyStart >= stompFrame.Length) return null;

                var body = stompFrame.Substring(bodyStart);
                // Remove null terminator if present
                if (body.EndsWith("\0"))
                    body = body.Substring(0, body.Length - 1);

                return body;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LIVE-STREAM] Parse error: {ex.Message}");
                return null;
            }
        }

        private async Task ProcessMessageBodyAsync(string body)
        {
            try
            {
                var settings = new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                };
                var message = JsonConvert.DeserializeObject<dynamic>(body, settings);
                if (message == null) return;

                string action = message.action != null ? (string)message.action : "";
                string status = message.status != null ? (string)message.status : "";

                Debug.WriteLine($"[LIVE-STREAM] Message - Action: {action}, Status: {status}");

                if (message.streamId != null)
                {
                    var streamId = (string)message.streamId;
                    Debug.WriteLine($"[LIVE-STREAM] Stream: {streamId}, Status: {status}");

                    if ((status == "REQUESTED" || status == "STARTING") && !_isStreaming)
                    {
                        int quality = message.quality != null ? (int)message.quality : 50;
                        int fps = message.fps != null ? (int)message.fps : 5;
                        await StartStreamingAsync(streamId, quality, fps);
                    }
                    else if (action == "STOP" && streamId == _currentStreamId)
                    {
                        await StopStreamingAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LIVE-STREAM] Error processing body: {ex.Message}");
            }
        }

        private async Task SubscribeToChannelsAsync()
        {
            if (_webSocket?.State != WebSocketState.Open || !_isStompConnected) return;

            // Subscribe to screenshot requests
            var screenshotDestination = $"/topic/session/{_sessionId}/screenshot-request";
            var screenshotSub = BuildSubscribeFrame(screenshotDestination, _subscriptionId++);
            await SendRawFrameAsync(screenshotSub);
            Debug.WriteLine($"[LIVE-STREAM] Subscribed to: {screenshotDestination}");

            // Subscribe to stream requests
            var streamDestination = $"/topic/session/{_sessionId}/stream-request";
            var streamSub = BuildSubscribeFrame(streamDestination, _subscriptionId++);
            await SendRawFrameAsync(streamSub);
            Debug.WriteLine($"[LIVE-STREAM] Subscribed to: {streamDestination}");
        }

        private string BuildSubscribeFrame(string destination, int id)
        {
            // STOMP SUBSCRIBE frame with CRLF line endings
            return $"SUBSCRIBE\r\nid:{id}\r\ndestination:{destination}\r\nack:auto\r\n\r\n\0";
        }

        private async Task StartPollingPendingRequestsAsync()
        {
            if (_pollingTimer != null) return;

            // Initial check after delay
            await Task.Delay(2000);
            await CheckPendingRequestsAsync();

            _pollingTimer = new Timer(async _ => await CheckPendingRequestsAsync(),
                null, 5000, 10000);
        }

        private async Task CheckPendingRequestsAsync()
        {
            lock (_lockObject)
            {
                if (_isStreaming || _isDisposed) return;
            }

            try
            {
                Debug.WriteLine($"[LIVE-STREAM] Checking pending requests for session {_sessionId}");
                var response = await _trackingService.GetPendingLiveStreamRequests(_sessionId);

                if (response.Status == 200 && response.Data != null && response.Data.Count > 0)
                {
                    foreach (var request in response.Data)
                    {
                        if ((request.Status == "REQUESTED" || request.Status == "STARTING") && !_isStreaming)
                        {
                            Debug.WriteLine($"[LIVE-STREAM] Found pending stream request: {request.StreamId}");
                            await StartStreamingAsync(request.StreamId, request.Quality ?? 50, request.Fps ?? 5);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LIVE-STREAM] Error checking requests: {ex.Message}");
            }
        }

        private async Task StartStreamingAsync(string streamId, int quality, int fps)
        {
            lock (_lockObject)
            {
                if (_isStreaming) return;
                _currentStreamId = streamId;
                _frameNumber = 0;
                _isStreaming = true;
                _quality = Math.Clamp(quality, 10, 100);
                _targetFps = Math.Clamp(fps, 1, 30);
                _frameIntervalMs = 1000 / _targetFps;
                _framesSent = 0;
                _lastStatsTime = DateTime.Now;
            }

            try
            {
                await ConfirmStreamStartAsync(streamId);

                // Start frame capture timer
                _frameTimer?.Dispose();
                _frameTimer = new Timer(CaptureAndSendFrame, null, 0, _frameIntervalMs);

                Debug.WriteLine($"[LIVE-STREAM] Started streaming: {streamId}, Quality: {_quality}%, FPS: {_targetFps}");
                StatusChanged?.Invoke(this, $"Live streaming started (Quality: {_quality}%, FPS: {_targetFps})");
                StreamingStatusChanged?.Invoke(this, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LIVE-STREAM] Failed to start: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"Failed to start: {ex.Message}");
                await StopStreamOnServerAsync(streamId);
                lock (_lockObject)
                {
                    _isStreaming = false;
                    _currentStreamId = null;
                }
            }
        }

        private async void CaptureAndSendFrame(object state)
        {
            lock (_lockObject)
            {
                if (!_isStreaming || !_isConnected || _webSocket?.State != WebSocketState.Open || !_isStompConnected)
                    return;
            }

            try
            {
                // Throttle frame rate
                var now = DateTime.Now;
                if ((now - _lastFrameTime).TotalMilliseconds < _frameIntervalMs)
                    return;
                _lastFrameTime = now;

                var imageBytes = CaptureScreen();
                if (imageBytes == null || imageBytes.Length == 0) return;

                _frameNumber++;
                _framesSent++;

                var imageBase64 = Convert.ToBase64String(imageBytes);
                var isKeyFrame = _frameNumber % 30 == 1;

                var frameData = new
                {
                    streamId = _currentStreamId,
                    imageData = imageBase64,
                    sequenceNumber = _frameNumber,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    width = (int)SystemParameters.PrimaryScreenWidth,
                    height = (int)SystemParameters.PrimaryScreenHeight,
                    isKeyFrame = isKeyFrame
                };

                await SendFrameToServerAsync(frameData);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LIVE-STREAM] Frame capture error: {ex.Message}");
            }
        }

        private byte[] CaptureScreen()
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

                    var encoderParameters = new EncoderParameters(1);
                    encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, _quality);

                    var jpegCodec = ImageCodecInfo.GetImageEncoders()
                        .FirstOrDefault(c => c.MimeType == "image/jpeg");

                    using (var ms = new MemoryStream())
                    {
                        if (jpegCodec != null)
                        {
                            bitmap.Save(ms, jpegCodec, encoderParameters);
                        }
                        else
                        {
                            bitmap.Save(ms, ImageFormat.Png);
                        }
                        return ms.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LIVE-STREAM] Screen capture failed: {ex.Message}");
                return null;
            }
        }

        private Rectangle GetScreenBounds()
        {
            return new Rectangle(0, 0,
                (int)SystemParameters.PrimaryScreenWidth,
                (int)SystemParameters.PrimaryScreenHeight);
        }

        private async Task SendFrameToServerAsync(object frameData)
        {
            if (_webSocket?.State != WebSocketState.Open || !_isStompConnected) return;

            lock (_sendLock)
            {
                try
                {
                    var frameJson = JsonConvert.SerializeObject(frameData, new JsonSerializerSettings
                    {
                        ContractResolver = new CamelCasePropertyNamesContractResolver()
                    });

                    // Build SEND frame with CRLF line endings
                    var frameContent = Encoding.UTF8.GetBytes(frameJson);
                    var sendFrame = $"SEND\r\ndestination:/app/live-screen/frame\r\ncontent-type:application/json\r\ncontent-length:{frameContent.Length}\r\n\r\n{frameJson}\0";

                    var bytes = Encoding.UTF8.GetBytes(sendFrame);
                    _webSocket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        true,
                        _cts.Token).Wait(100);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LIVE-STREAM] Send frame error: {ex.Message}");
                }
            }
        }

        private void StartKeepAlive()
        {
            _keepAliveTimer?.Dispose();
            _keepAliveTimer = new Timer(async _ => await SendHeartbeatAsync(), null, 10000, 10000);
        }

        private async Task SendHeartbeatAsync()
        {
            if (_webSocket?.State == WebSocketState.Open && _isStompConnected)
            {
                try
                {
                    // Send STOMP heartbeat (just a newline)
                    await SendRawFrameAsync("\r\n");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LIVE-STREAM] Heartbeat error: {ex.Message}");
                }
            }
        }

        private async Task ConfirmStreamStartAsync(string streamId)
        {
            try
            {
                var response = await _trackingService.ConfirmLiveStreamStart(streamId);
                if (response.Status != 200 && response.Status != 201)
                {
                    Debug.WriteLine($"[LIVE-STREAM] Confirm failed: {response.Message}");
                }
                else
                {
                    Debug.WriteLine($"[LIVE-STREAM] Stream {streamId} confirmed");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LIVE-STREAM] Confirm error: {ex.Message}");
            }
        }

        private async Task StopStreamOnServerAsync(string streamId)
        {
            try
            {
                await _trackingService.StopLiveStream(streamId);
                Debug.WriteLine($"[LIVE-STREAM] Stream {streamId} stopped on server");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LIVE-STREAM] Stop error: {ex.Message}");
            }
        }

        public async Task StopStreamingAsync()
        {
            string streamIdToStop = null;
            lock (_lockObject)
            {
                if (!_isStreaming) return;
                streamIdToStop = _currentStreamId;
                _isStreaming = false;
                _currentStreamId = null;
            }

            _frameTimer?.Dispose();
            _frameTimer = null;

            if (streamIdToStop != null)
            {
                await StopStreamOnServerAsync(streamIdToStop);
            }

            Debug.WriteLine("[LIVE-STREAM] Streaming stopped");
            StatusChanged?.Invoke(this, "Live streaming stopped");
            StreamingStatusChanged?.Invoke(this, false);
        }

        private async Task ScheduleReconnect()
        {
            if (_reconnectAttempts >= MAX_RECONNECT_ATTEMPTS)
            {
                Debug.WriteLine("[LIVE-STREAM] Max reconnections reached");
                ErrorOccurred?.Invoke(this, "Max reconnection attempts reached");
                return;
            }

            _reconnectAttempts++;
            var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, _reconnectAttempts), 30));

            Debug.WriteLine($"[LIVE-STREAM] Reconnecting in {delay.TotalSeconds}s (attempt {_reconnectAttempts})");

            await Task.Delay(delay);
            if (!_isConnected && !_isDisposed)
            {
                await ConnectAsync();
            }
        }

        public void UpdateQuality(int quality)
        {
            _quality = Math.Clamp(quality, 10, 100);
        }

        public void UpdateFps(int fps)
        {
            _targetFps = Math.Clamp(fps, 1, 30);
            _frameIntervalMs = 1000 / _targetFps;

            if (_isStreaming && _frameTimer != null)
            {
                _frameTimer.Change(0, _frameIntervalMs);
            }
        }

        public async Task DisconnectAsync()
        {
            await StopStreamingAsync();

            _pollingTimer?.Dispose();
            _pollingTimer = null;
            _keepAliveTimer?.Dispose();
            _keepAliveTimer = null;

            if (_webSocket?.State == WebSocketState.Open && _isStompConnected)
            {
                try
                {
                    var disconnectFrame = "DISCONNECT\r\n\r\n\0";
                    await SendRawFrameAsync(disconnectFrame);
                    await Task.Delay(100);
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LIVE-STREAM] Disconnect error: {ex.Message}");
                }
            }

            _cts?.Cancel();
            _webSocket?.Dispose();
            _isConnected = false;
            _isStompConnected = false;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _pollingTimer?.Dispose();
            _frameTimer?.Dispose();
            _keepAliveTimer?.Dispose();
            _cts?.Cancel();
            _webSocket?.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}