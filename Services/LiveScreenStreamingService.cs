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
        private bool _isSubscribed = false;

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
        }

        public async Task ConnectAsync()
        {
            if (_isDisposed) return;

            var wsUrl = BuildWebSocketUrl();
            Debug.WriteLine($"Connecting to WebSocket: {wsUrl}");

            try
            {
                _cts = new CancellationTokenSource();
                _webSocket = new ClientWebSocket();

                // Add required WebSocket headers
                _webSocket.Options.SetRequestHeader("Origin", "http://localhost:2027");
                _webSocket.Options.SetRequestHeader("User-Agent", "DesktopAgent/1.0");

                // Add authorization header
                var token = _tokenManager.CurrentToken?.Token;
                if (!string.IsNullOrEmpty(token))
                {
                    _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {token}");
                }

                // Set keep-alive interval
                _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

                // Connect with proper WebSocket subprotocol
                await _webSocket.ConnectAsync(new Uri(wsUrl), _cts.Token);

                if (_webSocket.State == WebSocketState.Open)
                {
                    _isConnected = true;
                    _reconnectAttempts = 0;
                    Debug.WriteLine("WebSocket connected successfully");
                    StatusChanged?.Invoke(this, "WebSocket connected");

                    // Start message receiver loop
                    _ = Task.Run(ReceiveMessagesAsync);

                    // Send STOMP CONNECT frame
                    await SendStompConnectAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to connect WebSocket: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"Connection failed: {ex.Message}");
                await ScheduleReconnect();
            }
        }

        private string BuildWebSocketUrl()
        {
            var baseUrl = _serverUrl;

            if (baseUrl.StartsWith("https://"))
            {
                baseUrl = baseUrl.Replace("https://", "wss://");
            }
            else if (baseUrl.StartsWith("http://"))
            {
                baseUrl = baseUrl.Replace("http://", "ws://");
            }
            else
            {
                baseUrl = "ws://" + baseUrl;
            }

            baseUrl = baseUrl.TrimEnd('/');

            // Remove any existing path and add correct endpoint
            if (baseUrl.Contains("/ws/live-screen"))
            {
                baseUrl = baseUrl.Substring(0, baseUrl.IndexOf("/ws/live-screen"));
            }

            baseUrl = baseUrl + "/ws/live-screen";

            // Don't add token to URL for now - use header instead
            // Some servers reject URLs with query parameters for WebSocket upgrade

            Debug.WriteLine($"WebSocket URL: {baseUrl}");
            return baseUrl;
        }

        private async Task SendStompConnectAsync()
        {
            if (_webSocket?.State != WebSocketState.Open) return;

            var token = _tokenManager.CurrentToken?.Token;

            var connectFrame = new StringBuilder();
            connectFrame.AppendLine("CONNECT");
            connectFrame.AppendLine("accept-version:1.2,1.1,1.0");
            connectFrame.AppendLine("heart-beat:10000,10000");
            if (!string.IsNullOrEmpty(token))
            {
                connectFrame.AppendLine($"Authorization:Bearer {token}");
            }
            connectFrame.AppendLine();
            connectFrame.Append("\0");

            await SendRawFrameAsync(connectFrame.ToString());
            Debug.WriteLine("Sent STOMP CONNECT frame");

            // Start keep-alive timer for heart-beat
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
                Debug.WriteLine($"Error sending frame: {ex.Message}");
            }
        }

        private async Task ReceiveMessagesAsync()
        {
            var buffer = new byte[65536];
            var messageBuilder = new StringBuilder();

            try
            {
                while (_webSocket?.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _webSocket.ReceiveAsync(
                            new ArraySegment<byte>(buffer),
                            _cts.Token);

                        var messagePart = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        messageBuilder.Append(messagePart);

                    } while (!result.EndOfMessage);

                    var fullMessage = messageBuilder.ToString();
                    messageBuilder.Clear();

                    if (!string.IsNullOrEmpty(fullMessage))
                    {
                        await ProcessReceivedMessageAsync(fullMessage);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Message receive cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error receiving message: {ex.Message}");
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
                Debug.WriteLine($"Received: {message.Substring(0, Math.Min(200, message.Length))}");

                if (message.StartsWith("CONNECTED"))
                {
                    Debug.WriteLine("STOMP CONNECTED successfully");
                    _isSubscribed = true;
                    await SubscribeToSessionChannelAsync();
                    await StartPollingPendingRequestsAsync();

                    StatusChanged?.Invoke(this, "STOMP connected and subscribed");
                }
                else if (message.StartsWith("MESSAGE"))
                {
                    var body = ParseStompFrameBody(message);
                    if (!string.IsNullOrEmpty(body))
                    {
                        await ProcessMessageBodyAsync(body);
                    }
                }
                else if (message.StartsWith("RECEIPT"))
                {
                    Debug.WriteLine("STOMP receipt received");
                }
                else if (message.StartsWith("ERROR"))
                {
                    Debug.WriteLine($"STOMP error: {message}");
                    ErrorOccurred?.Invoke(this, $"STOMP error: {message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing message: {ex.Message}");
            }
        }

        private string ParseStompFrameBody(string stompFrame)
        {
            try
            {
                // Find the double newline that separates headers from body
                int headerEndIndex = stompFrame.IndexOf("\n\n");
                if (headerEndIndex == -1) return null;

                int bodyStart = headerEndIndex + 2;
                if (bodyStart >= stompFrame.Length) return null;

                string body = stompFrame.Substring(bodyStart);

                // Remove trailing null character
                if (body.EndsWith("\0"))
                    body = body.Substring(0, body.Length - 1);

                return body;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing STOMP frame: {ex.Message}");
                return null;
            }
        }

        private async Task ProcessMessageBodyAsync(string body)
        {
            try
            {
                Debug.WriteLine($"Processing: {body}");
                var message = JsonConvert.DeserializeObject<dynamic>(body);

                if (message == null) return;

                string action = message.action != null ? (string)message.action : "";
                string status = message.status != null ? (string)message.status : "";

                if (message.streamId != null)
                {
                    var streamId = (string)message.streamId;
                    Debug.WriteLine($"Stream: {streamId}, Status: {status}, Action: {action}");

                    if (status == "REQUESTED" && !_isStreaming)
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
                Debug.WriteLine($"Error processing message body: {ex.Message}");
            }
        }

        private async Task SubscribeToSessionChannelAsync()
        {
            if (_webSocket?.State != WebSocketState.Open) return;

            // Subscribe to session-specific topic
            var destination = $"/topic/session/{_sessionId}/live-screen";
            var subscribeFrame = new StringBuilder();
            subscribeFrame.AppendLine("SUBSCRIBE");
            subscribeFrame.AppendLine($"id:{_subscriptionId++}");
            subscribeFrame.AppendLine($"destination:{destination}");
            subscribeFrame.AppendLine("ack:auto");
            subscribeFrame.AppendLine();
            subscribeFrame.Append("\0");

            await SendRawFrameAsync(subscribeFrame.ToString());
            Debug.WriteLine($"Subscribed to: {destination}");

            // Also subscribe to user queue
            var adminDestination = $"/user/queue/live-screen/status";
            subscribeFrame.Clear();
            subscribeFrame.AppendLine("SUBSCRIBE");
            subscribeFrame.AppendLine($"id:{_subscriptionId++}");
            subscribeFrame.AppendLine($"destination:{adminDestination}");
            subscribeFrame.AppendLine("ack:auto");
            subscribeFrame.AppendLine();
            subscribeFrame.Append("\0");

            await SendRawFrameAsync(subscribeFrame.ToString());
            Debug.WriteLine($"Subscribed to: {adminDestination}");
        }

        private async Task SendFrameToServerAsync(StreamFrameDto frame)
        {
            if (_webSocket?.State != WebSocketState.Open || !_isSubscribed) return;

            lock (_sendLock)
            {
                try
                {
                    var frameJson = JsonConvert.SerializeObject(frame);

                    var sendFrame = new StringBuilder();
                    sendFrame.AppendLine("SEND");
                    sendFrame.AppendLine("destination:/app/live-screen/frame");
                    sendFrame.AppendLine("content-type:application/json");
                    sendFrame.AppendLine($"content-length:{Encoding.UTF8.GetBytes(frameJson).Length}");
                    sendFrame.AppendLine();
                    sendFrame.Append(frameJson);
                    sendFrame.Append("\0");

                    var bytes = Encoding.UTF8.GetBytes(sendFrame.ToString());
                    _webSocket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        true,
                        _cts.Token).Wait();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error sending frame: {ex.Message}");
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
            if (_webSocket?.State == WebSocketState.Open && _isSubscribed)
            {
                try
                {
                    // Send STOMP heartbeat (newline)
                    await SendRawFrameAsync("\n");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Heartbeat error: {ex.Message}");
                }
            }
        }

        private async Task StartPollingPendingRequestsAsync()
        {
            if (_pollingTimer != null) return;

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
                var response = await _trackingService.GetPendingLiveStreamRequests(_sessionId);
                if (response.Status == 200 && response.Data != null && response.Data.Count > 0)
                {
                    foreach (var request in response.Data)
                    {
                        if ((request.Status == "REQUESTED" || request.Status == "STARTING") && !_isStreaming)
                        {
                            Debug.WriteLine($"Found pending stream request: {request.StreamId}");
                            await StartStreamingAsync(request.StreamId, 50, 5);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking pending requests: {ex.Message}");
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
            }

            try
            {
                await ConfirmStreamStartAsync(streamId);

                int intervalMs = 1000 / _targetFps;
                _frameTimer?.Dispose();
                _frameTimer = new Timer(CaptureAndSendFrame, null, 0, intervalMs);

                Debug.WriteLine($"Started streaming: {streamId}, Quality: {_quality}, FPS: {_targetFps}");
                StatusChanged?.Invoke(this, $"Live streaming started: {streamId}");
                StreamingStatusChanged?.Invoke(this, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to start streaming: {ex.Message}");
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
                if (!_isStreaming || !_isConnected || _webSocket?.State != WebSocketState.Open) return;
            }

            try
            {
                var imageBytes = CaptureScreen();

                if (imageBytes != null && imageBytes.Length > 0)
                {
                    _frameNumber++;
                    var imageBase64 = Convert.ToBase64String(imageBytes);
                    var isKeyFrame = _frameNumber % 30 == 1;

                    var frame = new StreamFrameDto
                    {
                        StreamId = _currentStreamId,
                        ImageBase64 = imageBase64,
                        FrameNumber = _frameNumber,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Width = (int)SystemParameters.PrimaryScreenWidth,
                        Height = (int)SystemParameters.PrimaryScreenHeight,
                        IsKeyFrame = isKeyFrame
                    };

                    await SendFrameToServerAsync(frame);

                    if (_frameNumber % 100 == 0)
                    {
                        Debug.WriteLine($"Sent frame {_frameNumber}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Frame capture error: {ex.Message}");
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
                Debug.WriteLine($"Screen capture failed: {ex.Message}");
                return null;
            }
        }

        private Rectangle GetScreenBounds()
        {
            return new Rectangle(0, 0,
                (int)SystemParameters.PrimaryScreenWidth,
                (int)SystemParameters.PrimaryScreenHeight);
        }

        private async Task ConfirmStreamStartAsync(string streamId)
        {
            try
            {
                var response = await _trackingService.ConfirmLiveStreamStart(streamId);
                if (response.Status != 200 && response.Status != 201)
                {
                    Debug.WriteLine($"Failed to confirm stream start: {response.Message}");
                }
                else
                {
                    Debug.WriteLine($"Stream {streamId} confirmed");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error confirming stream: {ex.Message}");
            }
        }

        private async Task StopStreamOnServerAsync(string streamId)
        {
            try
            {
                await _trackingService.StopLiveStream(streamId);
                Debug.WriteLine($"Stream {streamId} stopped on server");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error stopping stream: {ex.Message}");
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

            Debug.WriteLine("Streaming stopped");
            StatusChanged?.Invoke(this, "Live streaming stopped");
            StreamingStatusChanged?.Invoke(this, false);
        }

        private async Task ScheduleReconnect()
        {
            if (_reconnectAttempts >= MAX_RECONNECT_ATTEMPTS)
            {
                Debug.WriteLine("Max reconnection attempts reached");
                ErrorOccurred?.Invoke(this, "Max reconnection attempts reached");
                return;
            }

            _reconnectAttempts++;
            var delay = TimeSpan.FromSeconds(Math.Pow(2, _reconnectAttempts));
            Debug.WriteLine($"Reconnecting in {delay.TotalSeconds}s (attempt {_reconnectAttempts})");

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
            if (_isStreaming && _frameTimer != null)
            {
                int intervalMs = 1000 / _targetFps;
                _frameTimer.Change(0, intervalMs);
            }
        }

        public async Task DisconnectAsync()
        {
            await StopStreamingAsync();

            _pollingTimer?.Dispose();
            _pollingTimer = null;
            _keepAliveTimer?.Dispose();
            _keepAliveTimer = null;

            if (_webSocket?.State == WebSocketState.Open)
            {
                try
                {
                    var disconnectFrame = new StringBuilder();
                    disconnectFrame.AppendLine("DISCONNECT");
                    disconnectFrame.AppendLine();
                    disconnectFrame.Append("\0");
                    await SendRawFrameAsync(disconnectFrame.ToString());
                    await Task.Delay(100);
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error during disconnect: {ex.Message}");
                }
            }

            _cts?.Cancel();
            _webSocket?.Dispose();
            _isConnected = false;
            _isSubscribed = false;
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