
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using Microsoft.Win32;
using monitor_desktop.Models.ActivityMonitoring;
using monitor_desktop.Models.Enums;

namespace monitor_desktop.Services
{
    public class ActivityTrackerService : IDisposable
    {
        private static ActivityTrackerService _instance;
        private static readonly object _instanceLock = new object();

        private readonly ActivityTrackingService _trackingService;
        private readonly TokenManager _tokenManager;

        private bool _isTracking;
        private long _currentSessionId;
        private readonly object _lockObject = new object();
        private bool _isDisposed;

        public bool IsDisposed => _isDisposed;

        // Mouse & Keyboard counters
        private int _totalMouseClicks;
        private int _totalMouseMovements;
        private int _totalKeystrokes;
        private Timer _mouseKeyboardTimer;
        private readonly int _mouseKeyboardSendIntervalMinutes = 10;

        // Idle tracking
        private DateTime _lastActivityTime = DateTime.Now;
        private bool _isIdle;
        private DateTime _idleStartTime;
        private Timer _idleCheckTimer;
        private bool _isTrackingPaused;

        // Application tracking
        private string _currentAppName;
        private string _currentWindowTitle;
        private DateTime _currentAppStartTime;
        private int _currentAppFocusCount;
        private bool _isAppTrackingPaused;

        // Browser tracking
        private bool _isBrowserActive;
        private string _currentBrowserName;
        private DateTime _currentBrowserStartTime;
        private long? _currentBrowserTempId;
        private bool _isBrowserTrackingPaused;

        // URL tracking
        private string _currentUrl;
        private string _currentUrlTitle;
        private string _currentUrlDomain;
        private DateTime _currentUrlStartTime;
        private List<PendingUrlVisit> _pendingUrlVisits = new List<PendingUrlVisit>();
        private bool _isUrlTrackingPaused;

        // Mouse/keyboard hooks
        private LowLevelKeyboardProc _keyboardProc;
        private LowLevelMouseProc _mouseProc;
        private IntPtr _keyboardHookId = IntPtr.Zero;
        private IntPtr _mouseHookId = IntPtr.Zero;

        // Configuration
        private int _idleThresholdSeconds = 120;
        private bool _isSystemSleeping;

        // Heartbeat
        private Timer _heartbeatTimer;

        public event EventHandler<string> StatusChanged;

        public bool IsTracking => _isTracking;
        public long CurrentSessionId => _currentSessionId;
        public bool IsIdle => _isIdle;

        private class PendingUrlVisit
        {
            public string Url { get; set; }
            public string PageTitle { get; set; }
            public string Domain { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public int DurationSeconds { get; set; }
            public UrlCategory Category { get; set; }
            public bool IsProductive { get; set; }
            public int VisitCount { get; set; }
        }

        public static ActivityTrackerService GetInstance(ActivityTrackingService trackingService, TokenManager tokenManager)
        {
            lock (_instanceLock)
            {
                if (_instance == null || _instance._isDisposed)
                {
                    _instance?.Dispose();
                    _instance = new ActivityTrackerService(trackingService, tokenManager);
                }
                return _instance;
            }
        }

        public static ActivityTrackerService GetExistingInstance()
        {
            lock (_instanceLock)
            {
                return _instance;
            }
        }

        public static void DisposeInstance()
        {
            lock (_instanceLock)
            {
                if (_instance != null)
                {
                    _instance.StopTrackingAsync(true).Wait(500);
                    _instance.Dispose();
                    _instance = null;
                }
            }
        }

        private ActivityTrackerService(ActivityTrackingService trackingService, TokenManager tokenManager)
        {
            _trackingService = trackingService;
            _tokenManager = tokenManager;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
        }

        private void AddDebugLog(string message)
        {
            var logEntry = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            Debug.WriteLine(logEntry);
            StatusChanged?.Invoke(this, logEntry);
        }

        #region Native Methods

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out Point lpPoint);

        [DllImport("user32.dll")]
        private static extern uint GetLastInputInfo(ref LASTINPUTINFO plii);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MOUSEMOVE = 0x0200;

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        private static uint GetLastInputTime()
        {
            LASTINPUTINFO lii = new LASTINPUTINFO();
            lii.cbSize = (uint)Marshal.SizeOf(lii);
            GetLastInputInfo(ref lii);
            return lii.dwTime;
        }

        private static uint GetIdleTime()
        {
            return (uint)Environment.TickCount - GetLastInputTime();
        }

        #endregion

        #region System Event Handlers

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            switch (e.Mode)
            {
                case PowerModes.Suspend:
                    _isSystemSleeping = true;
                    AddDebugLog("System entering sleep mode - pausing tracking");
                    PauseTrackingForIdle();
                    break;
                case PowerModes.Resume:
                    _isSystemSleeping = false;
                    AddDebugLog("System resumed from sleep - resuming tracking");
                    _lastActivityTime = DateTime.Now;
                    if (_isIdle) ResumeTrackingAfterIdle();
                    break;
            }
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                    AddDebugLog("Workstation locked - pausing tracking");
                    PauseTrackingForIdle();
                    break;
                case SessionSwitchReason.SessionUnlock:
                    AddDebugLog("Workstation unlocked - resuming tracking");
                    _lastActivityTime = DateTime.Now;
                    if (_isIdle) ResumeTrackingAfterIdle();
                    break;
            }
        }

        #endregion

        #region Idle Tracking

        private void PauseTrackingForIdle()
        {
            if (!_isTracking || _isTrackingPaused) return;
            _isTrackingPaused = true;
            _isAppTrackingPaused = true;
            _isBrowserTrackingPaused = true;
            _isUrlTrackingPaused = true;
            AddDebugLog("TRACKING PAUSED - No activity will be recorded until idle ends");
        }

        private void ResumeTrackingAfterIdle()
        {
            if (!_isTracking || !_isTrackingPaused) return;
            _isTrackingPaused = false;
            _isAppTrackingPaused = false;
            _isBrowserTrackingPaused = false;
            _isUrlTrackingPaused = false;

            if (!string.IsNullOrEmpty(_currentAppName)) _currentAppStartTime = DateTime.Now;
            if (_isBrowserActive) _currentBrowserStartTime = DateTime.Now;
            if (!string.IsNullOrEmpty(_currentUrl)) _currentUrlStartTime = DateTime.Now;

            AddDebugLog("TRACKING RESUMED - Activity recording continues");
        }

        private void ResetIdleState()
        {
            _lastActivityTime = DateTime.Now;

            if (_isIdle)
            {
                var idleEndTime = DateTime.Now;
                var idleDurationSeconds = (int)(idleEndTime - _idleStartTime).TotalSeconds;

                if (idleDurationSeconds >= _idleThresholdSeconds)
                {
                    AddDebugLog($"Idle ended - Duration: {idleDurationSeconds}s");
                    _ = SendIdlePeriodAsync(_idleStartTime, idleEndTime, idleDurationSeconds);
                }

                _isIdle = false;
                ResumeTrackingAfterIdle();
            }
        }

        private void CheckIdleState()
        {
            uint systemIdleTimeMs = GetIdleTime();
            double systemIdleSeconds = systemIdleTimeMs / 1000.0;
            var manualIdleTime = DateTime.Now - _lastActivityTime;
            double effectiveIdleSeconds = Math.Max(systemIdleSeconds, manualIdleTime.TotalSeconds);

            if (!_isIdle && effectiveIdleSeconds >= _idleThresholdSeconds)
            {
                _isIdle = true;
                _idleStartTime = DateTime.Now;
                AddDebugLog($"Idle started at {_idleStartTime:HH:mm:ss} (System idle: {effectiveIdleSeconds:F0}s)");
                PauseTrackingForIdle();
            }
            else if (_isIdle && effectiveIdleSeconds < _idleThresholdSeconds)
            {
                ResetIdleState();
            }
        }

        private async Task SendIdlePeriodAsync(DateTime startTime, DateTime endTime, int durationSeconds)
        {
            if (!_isTracking || _currentSessionId == 0) return;

            try
            {
                var request = new IdlePeriodRequest
                {
                    SessionId = _currentSessionId,
                    IdleStart = startTime,
                    IdleEnd = endTime,
                    TriggerReason = IdleTrigger.NO_INPUT,
                    DurationSeconds = durationSeconds
                };

                var response = await _trackingService.LogIdlePeriod(request);
                if (response.Status == 200 || response.Status == 201)
                    AddDebugLog($"✓ Idle period sent: {durationSeconds}s");
                else
                    AddDebugLog($"✗ Failed to send idle period: {response.Message}");
            }
            catch (Exception ex)
            {
                AddDebugLog($"✗ Error sending idle period: {ex.Message}");
            }
        }

        #endregion

        #region Browser URL Extraction

        private bool IsBrowserProcess(string processName)
        {
            var browsers = new[] { "chrome", "firefox", "edge", "msedge", "opera", "brave", "browser" };
            return browsers.Any(b => processName.ToLower().Contains(b));
        }

        private string GetBrowserNameFromProcess(string processName)
        {
            processName = processName.ToLower();
            if (processName.Contains("chrome")) return "Chrome";
            if (processName.Contains("firefox")) return "Firefox";
            if (processName.Contains("edge") || processName.Contains("msedge")) return "Edge";
            if (processName.Contains("opera")) return "Opera";
            if (processName.Contains("brave")) return "Brave";
            return processName;
        }

        private string GetBrowserUrl(IntPtr hWnd, string browserType)
        {
            try
            {
                if (browserType.Contains("chrome") || browserType.Contains("edge"))
                {
                    return GetChromiumUrl(hWnd);
                }
                else if (browserType.Contains("firefox"))
                {
                    return GetFirefoxUrl(hWnd);
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private string GetChromiumUrl(IntPtr hWnd)
        {
            try
            {
                var element = AutomationElement.FromHandle(hWnd);
                if (element == null) return null;

                var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
                var addressBar = element.FindFirst(TreeScope.Descendants, condition);

                if (addressBar != null)
                {
                    var valuePattern = addressBar.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
                    if (valuePattern != null)
                    {
                        var url = valuePattern.Current.Value;
                        if (!string.IsNullOrEmpty(url) && (url.StartsWith("http://") || url.StartsWith("https://")))
                            return url;
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private string GetFirefoxUrl(IntPtr hWnd)
        {
            try
            {
                var element = AutomationElement.FromHandle(hWnd);
                if (element == null) return null;

                System.Windows.Automation.Condition condition = new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                    new PropertyCondition(AutomationElement.NameProperty, "Search or enter address")
                );

                var addressBar = element.FindFirst(TreeScope.Descendants, condition);
                if (addressBar == null)
                {
                    condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
                    addressBar = element.FindFirst(TreeScope.Descendants, condition);
                }

                if (addressBar != null)
                {
                    var valuePattern = addressBar.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
                    if (valuePattern != null)
                    {
                        var url = valuePattern.Current.Value;
                        if (!string.IsNullOrEmpty(url) && (url.StartsWith("http://") || url.StartsWith("https://")))
                            return url;
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private string ExtractDomain(string url)
        {
            try
            {
                var uri = new Uri(url);
                return uri.Host.Replace("www.", "");
            }
            catch
            {
                return url.Split('/')[0];
            }
        }

        private UrlCategory GetUrlCategory(string domain, string url)
        {
            domain = domain.ToLower();

            if (domain.Contains("youtube") || domain.Contains("netflix") || domain.Contains("twitch"))
                return UrlCategory.ENTERTAINMENT;

            if (domain.Contains("facebook") || domain.Contains("twitter") || domain.Contains("instagram") ||
                domain.Contains("linkedin") || domain.Contains("reddit"))
                return UrlCategory.SOCIAL;

            if (domain.Contains("github") || domain.Contains("stackoverflow") || domain.Contains("gitlab"))
                return UrlCategory.DEVELOPMENT;

            if (domain.Contains("gmail") || domain.Contains("outlook"))
                return UrlCategory.EMAIL;

            if (domain.Contains("google") || domain.Contains("bing"))
                return UrlCategory.SEARCH;

            if (domain.Contains("amazon") || domain.Contains("ebay"))
                return UrlCategory.SHOPPING;

            return UrlCategory.OTHER;
        }

        private string ExtractUrlFromTitle(string windowTitle)
        {
            var patterns = new[] { "http://", "https://", "www." };

            foreach (var pattern in patterns)
            {
                var index = windowTitle.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    var url = windowTitle.Substring(index);
                    var endIndex = url.IndexOf(" - ", StringComparison.OrdinalIgnoreCase);
                    if (endIndex > 0)
                        url = url.Substring(0, endIndex);

                    endIndex = url.IndexOf(" | ", StringComparison.OrdinalIgnoreCase);
                    if (endIndex > 0)
                        url = url.Substring(0, endIndex);

                    return url.Trim();
                }
            }
            return null;
        }

        #endregion

        #region Application Tracking

        private void StartWindowMonitoring()
        {
            Task.Run(async () =>
            {
                AddDebugLog("Window monitoring started");
                while (_isTracking && !_isDisposed)
                {
                    await Task.Delay(1000);
                    if (!_isTracking || _isDisposed) break;

                    try
                    {
                        await Application.Current?.Dispatcher.InvokeAsync(() => TrackActiveWindow());
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error in window monitoring: {ex.Message}");
                    }
                }
            });
        }

        private void TrackActiveWindow()
        {
            if (_isTrackingPaused) return;

            try
            {
                IntPtr handle = GetForegroundWindow();
                if (handle == IntPtr.Zero) return;

                const int nChars = 256;
                StringBuilder buff = new StringBuilder(nChars);
                GetWindowText(handle, buff, nChars);
                string windowTitle = buff.ToString();

                GetWindowThreadProcessId(handle, out uint processId);
                string processName = "Unknown";

                try
                {
                    var process = Process.GetProcessById((int)processId);
                    processName = process.ProcessName;
                }
                catch { }

                bool isBrowser = IsBrowserProcess(processName);

                if (isBrowser)
                {
                    TrackBrowserActivity(handle, processName, windowTitle);
                }
                else
                {
                    TrackRegularApplication(processName, windowTitle);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error tracking window: {ex.Message}");
            }
        }

        private void TrackRegularApplication(string processName, string windowTitle)
        {
            if (_isAppTrackingPaused) return;

            string appKey = $"{processName}|{windowTitle}";
            string currentKey = $"{_currentAppName}|{_currentWindowTitle}";

            if (currentKey != appKey)
            {
                AddDebugLog($"App changed - New: {processName}");

                if (!string.IsNullOrEmpty(_currentAppName))
                {
                    SendApplicationUsageAndReset();
                }

                _currentAppName = processName;
                _currentWindowTitle = windowTitle;
                _currentAppStartTime = DateTime.Now;
                _currentAppFocusCount = 1;
            }
            else
            {
                _currentAppFocusCount++;
            }
        }

        private async void SendApplicationUsageAndReset()
        {
            if (string.IsNullOrEmpty(_currentAppName)) return;

            var endTime = DateTime.Now;
            var durationSeconds = (int)(endTime - _currentAppStartTime).TotalSeconds;

            if (durationSeconds > 0)
            {
                var request = new ApplicationUsageRequest
                {
                    SessionId = _currentSessionId,
                    AppName = _currentAppName,
                    AppPath = "",
                    AppVersion = "",
                    AppCategory = GetAppCategory(_currentAppName),
                    WindowTitle = _currentWindowTitle,
                    StartTime = _currentAppStartTime,
                    EndTime = endTime,
                    DurationSeconds = durationSeconds,
                    FocusCount = _currentAppFocusCount,
                    IsProductive = true
                };

                var response = await _trackingService.SaveApplicationUsage(request);
                if (response.Status == 200 || response.Status == 201)
                    AddDebugLog($"✓ App sent: {_currentAppName} ({durationSeconds}s)");
            }

            _currentAppName = null;
            _currentWindowTitle = null;
            _currentAppFocusCount = 0;
        }

        private AppCategory GetAppCategory(string appName)
        {
            appName = appName.ToLower();
            if (appName.Contains("visual studio") || appName.Contains("vscode") || appName.Contains("intellij")) return AppCategory.DEVELOPMENT;
            if (appName.Contains("excel") || appName.Contains("word") || appName.Contains("powerpoint") || appName.Contains("outlook")) return AppCategory.OFFICE;
            if (appName.Contains("slack") || appName.Contains("teams") || appName.Contains("discord") || appName.Contains("zoom")) return AppCategory.COMMUNICATION;
            if (appName.Contains("spotify") || appName.Contains("netflix") || appName.Contains("youtube")) return AppCategory.ENTERTAINMENT;
            return AppCategory.OTHER;
        }

        #endregion

        #region Browser and URL Tracking

        private async void TrackBrowserActivity(IntPtr handle, string processName, string windowTitle)
        {
            if (_isBrowserTrackingPaused) return;

            string browserName = GetBrowserNameFromProcess(processName);
            string currentUrl = GetBrowserUrl(handle, processName);

            if (string.IsNullOrEmpty(currentUrl))
            {
                currentUrl = ExtractUrlFromTitle(windowTitle);
            }

            string domain = !string.IsNullOrEmpty(currentUrl) ? ExtractDomain(currentUrl) : null;

            // Check if browser changed
            if (!_isBrowserActive || _currentBrowserName != browserName)
            {
                AddDebugLog($"Browser changed - New: {browserName}");

                if (_isBrowserActive)
                {
                    await SaveCurrentBrowserAndPendingUrls();
                }

                _isBrowserActive = true;
                _currentBrowserName = browserName;
                _currentBrowserStartTime = DateTime.Now;
                _currentBrowserTempId = DateTime.Now.Ticks;
                _pendingUrlVisits.Clear();

                AddDebugLog($"Started tracking browser: {browserName}");
            }

            // Check if URL changed
            if (!_isUrlTrackingPaused && !string.IsNullOrEmpty(currentUrl) && _currentUrl != currentUrl)
            {
                AddDebugLog($"URL changed - New: {domain}");

                if (!string.IsNullOrEmpty(_currentUrl))
                {
                    var endTime = DateTime.Now;
                    var durationSeconds = (int)(endTime - _currentUrlStartTime).TotalSeconds;

                    if (durationSeconds > 0)
                    {
                        var pendingVisit = new PendingUrlVisit
                        {
                            Url = _currentUrl,
                            PageTitle = _currentUrlTitle,
                            Domain = _currentUrlDomain,
                            StartTime = _currentUrlStartTime,
                            EndTime = endTime,
                            DurationSeconds = durationSeconds,
                            Category = GetUrlCategory(_currentUrlDomain, _currentUrl),
                            IsProductive = true,
                            VisitCount = 1
                        };
                        _pendingUrlVisits.Add(pendingVisit);
                        AddDebugLog($"Added pending URL: {_currentUrlDomain} ({durationSeconds}s)");
                    }
                }

                _currentUrl = currentUrl;
                _currentUrlTitle = windowTitle;
                _currentUrlDomain = domain;
                _currentUrlStartTime = DateTime.Now;

                AddDebugLog($"Started tracking URL: {domain}");
            }
        }

        private async Task SaveCurrentBrowserAndPendingUrls()
        {
            if (!_isBrowserActive || string.IsNullOrEmpty(_currentBrowserName)) return;

            var endTime = DateTime.Now;
            var durationSeconds = (int)(endTime - _currentBrowserStartTime).TotalSeconds;

            if (durationSeconds > 0)
            {
                var browserRequest = new BrowserUsageRequest
                {
                    SessionId = _currentSessionId,
                    BrowserName = _currentBrowserName,
                    BrowserVersion = "",
                    StartTime = _currentBrowserStartTime,
                    EndTime = endTime,
                    DurationSeconds = durationSeconds
                };

                AddDebugLog($"Saving browser: {_currentBrowserName} ({durationSeconds}s)");
                await _trackingService.SaveBrowserUsage(browserRequest);
                AddDebugLog($"✓ Browser saved: {_currentBrowserName}");
            }

            // Save all pending URL visits
            foreach (var visit in _pendingUrlVisits)
            {
                var urlRequest = new BrowserUrlVisitRequest
                {
                    BrowserUsageId = null,
                    Url = visit.Url,
                    PageTitle = visit.PageTitle,
                    Domain = visit.Domain,
                    Category = visit.Category,
                    VisitedAt = visit.EndTime,
                    TimeSpentSeconds = visit.DurationSeconds,
                    IsProductive = visit.IsProductive,
                    VisitCount = visit.VisitCount
                };

                AddDebugLog($"Saving URL: {visit.Domain} ({visit.DurationSeconds}s)");
                var response = await _trackingService.SaveBrowserUrlVisit(urlRequest);

                if (response.Status == 200 || response.Status == 201)
                {
                    AddDebugLog($"✓ URL saved: {visit.Domain}");
                }
                else
                {
                    AddDebugLog($"✗ Failed to save URL: {visit.Domain} - {response.Message}");
                }
            }

            // Save current URL if exists
            if (!string.IsNullOrEmpty(_currentUrl))
            {
                var currentDuration = (int)(endTime - _currentUrlStartTime).TotalSeconds;
                if (currentDuration > 0)
                {
                    var currentUrlRequest = new BrowserUrlVisitRequest
                    {
                        BrowserUsageId = null,
                        Url = _currentUrl,
                        PageTitle = _currentUrlTitle,
                        Domain = _currentUrlDomain,
                        Category = GetUrlCategory(_currentUrlDomain, _currentUrl),
                        VisitedAt = endTime,
                        TimeSpentSeconds = currentDuration,
                        IsProductive = true,
                        VisitCount = 1
                    };

                    AddDebugLog($"Saving current URL: {_currentUrlDomain} ({currentDuration}s)");
                    var response = await _trackingService.SaveBrowserUrlVisit(currentUrlRequest);

                    if (response.Status == 200 || response.Status == 201)
                    {
                        AddDebugLog($"✓ Current URL saved: {_currentUrlDomain}");
                    }
                    else
                    {
                        AddDebugLog($"✗ Failed to save current URL: {_currentUrlDomain} - {response.Message}");
                    }
                }
            }

            _isBrowserActive = false;
            _currentBrowserName = null;
            _currentBrowserTempId = null;
            _currentUrl = null;
            _currentUrlDomain = null;
            _pendingUrlVisits.Clear();
        }

        #endregion

        #region Mouse & Keyboard

        private void StartMouseKeyboardTimer()
        {
            _mouseKeyboardTimer = new Timer(SendMouseKeyboardData, null, TimeSpan.FromMinutes(_mouseKeyboardSendIntervalMinutes), TimeSpan.FromMinutes(_mouseKeyboardSendIntervalMinutes));
            AddDebugLog($"Mouse/Keyboard timer started - Interval: {_mouseKeyboardSendIntervalMinutes} minutes");
        }

        private async void SendMouseKeyboardData(object state)
        {
            if (!_isTracking || _currentSessionId == 0 || _isTrackingPaused) return;

            int clicks = Interlocked.Exchange(ref _totalMouseClicks, 0);
            int movements = Interlocked.Exchange(ref _totalMouseMovements, 0);
            int keystrokes = Interlocked.Exchange(ref _totalKeystrokes, 0);

            await SendMouseActivityAsync(clicks, movements);
            await SendKeyboardActivityAsync(keystrokes);
        }

        private async Task SendMouseActivityAsync(int clicks, int movements)
        {
            if (!_isTracking || _currentSessionId == 0) return;
            if (clicks == 0 && movements == 0) return;

            try
            {
                var request = new MouseActivityRequest
                {
                    SessionId = _currentSessionId,
                    RecordedAt = DateTime.Now,
                    IntervalSeconds = _mouseKeyboardSendIntervalMinutes * 60,
                    TotalClicks = clicks,
                    LeftClicks = clicks,
                    RightClicks = 0,
                    MiddleClicks = 0,
                    DoubleClicks = 0,
                    ScrollEvents = 0,
                    DistancePixels = 0
                };

                var response = await _trackingService.SaveMouseActivity(request);
                if (response.Status == 200 || response.Status == 201)
                    AddDebugLog($"✓ Mouse sent: {clicks} clicks");
            }
            catch (Exception ex)
            {
                AddDebugLog($"✗ Error sending mouse activity: {ex.Message}");
            }
        }

        private async Task SendKeyboardActivityAsync(int keystrokes)
        {
            if (!_isTracking || _currentSessionId == 0) return;
            if (keystrokes == 0) return;

            try
            {
                var request = new KeyboardActivityRequest
                {
                    SessionId = _currentSessionId,
                    RecordedAt = DateTime.Now,
                    IntervalSeconds = _mouseKeyboardSendIntervalMinutes * 60,
                    TotalKeystrokes = keystrokes,
                    SpecialKeyCount = 0,
                    TypingBursts = 0,
                    AvgWpm = 0,
                    PeakWpm = 0,
                    ActiveTypingSeconds = 0
                };

                var response = await _trackingService.SaveKeyboardActivity(request);
                if (response.Status == 200 || response.Status == 201)
                    AddDebugLog($"✓ Keyboard sent: {keystrokes} keystrokes");
            }
            catch (Exception ex)
            {
                AddDebugLog($"✗ Error sending keyboard activity: {ex.Message}");
            }
        }

        #endregion

        #region Hooks

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                if (!_isTrackingPaused) Interlocked.Increment(ref _totalKeystrokes);
                ResetIdleState();
            }
            return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                switch ((int)wParam)
                {
                    case WM_LBUTTONDOWN:
                    case WM_RBUTTONDOWN:
                    case WM_MBUTTONDOWN:
                        if (!_isTrackingPaused) Interlocked.Increment(ref _totalMouseClicks);
                        ResetIdleState();
                        break;
                    case WM_MOUSEMOVE:
                        if (!_isTrackingPaused) Interlocked.Increment(ref _totalMouseMovements);
                        ResetIdleState();
                        break;
                }
            }
            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        #endregion

        #region Start/Stop

        public void StartTracking(long sessionId, int? idleThresholdSeconds = null)
        {
            AddDebugLog($"=== START TRACKING - Session: {sessionId} ===");

            lock (_lockObject)
            {
                if (_isTracking) StopTrackingAsync(false).Wait();

                if (idleThresholdSeconds.HasValue) _idleThresholdSeconds = idleThresholdSeconds.Value;

                _currentSessionId = sessionId;
                _isTracking = true;
                _lastActivityTime = DateTime.Now;
                _isIdle = false;
                _isTrackingPaused = false;
                _isAppTrackingPaused = false;
                _isBrowserTrackingPaused = false;
                _isUrlTrackingPaused = false;

                _totalMouseClicks = 0;
                _totalMouseMovements = 0;
                _totalKeystrokes = 0;

                _currentAppName = null;
                _currentBrowserName = null;
                _currentBrowserTempId = null;
                _currentUrl = null;
                _isBrowserActive = false;
                _pendingUrlVisits.Clear();
            }

            _keyboardProc = KeyboardHookCallback;
            _mouseProc = MouseHookCallback;

            try
            {
                using (var curProcess = Process.GetCurrentProcess())
                using (var module = curProcess.MainModule)
                {
                    _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, GetModuleHandle(module.ModuleName), 0);
                    _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(module.ModuleName), 0);
                    AddDebugLog("Hooks installed");
                }
            }
            catch (Exception ex)
            {
                AddDebugLog($"Error setting up hooks: {ex.Message}");
            }

            StartWindowMonitoring();
            StartMouseKeyboardTimer();
            _idleCheckTimer = new Timer(_ => CheckIdleState(), null, 1000, 1000);
            _heartbeatTimer = new Timer(HeartbeatCallback, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

            AddDebugLog($"✓ Tracking started successfully");
        }

        private void HeartbeatCallback(object state)
        {
            if (!_isTracking || _isDisposed) return;
            AddDebugLog("Heartbeat - tracking active");
        }

        public async Task StopTrackingAsync(bool sendFinalData = true)
        {
            AddDebugLog($"=== STOP TRACKING ===");

            if (!_isTracking) return;

            if (sendFinalData && !_isTrackingPaused)
            {
                if (!string.IsNullOrEmpty(_currentAppName)) SendApplicationUsageAndReset();
                if (_isBrowserActive) await SaveCurrentBrowserAndPendingUrls();

                int clicks = Interlocked.Exchange(ref _totalMouseClicks, 0);
                int movements = Interlocked.Exchange(ref _totalMouseMovements, 0);
                int keystrokes = Interlocked.Exchange(ref _totalKeystrokes, 0);

                await SendMouseActivityAsync(clicks, movements);
                await SendKeyboardActivityAsync(keystrokes);
            }

            if (_isIdle)
            {
                var idleEndTime = DateTime.Now;
                var durationSeconds = (int)(idleEndTime - _idleStartTime).TotalSeconds;
                if (durationSeconds >= _idleThresholdSeconds)
                {
                    await SendIdlePeriodAsync(_idleStartTime, idleEndTime, durationSeconds);
                }
                _isIdle = false;
            }

            lock (_lockObject)
            {
                _heartbeatTimer?.Dispose();
                _heartbeatTimer = null;
                _mouseKeyboardTimer?.Dispose();
                _mouseKeyboardTimer = null;
                _idleCheckTimer?.Dispose();
                _idleCheckTimer = null;

                if (_keyboardHookId != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_keyboardHookId);
                    _keyboardHookId = IntPtr.Zero;
                }
                if (_mouseHookId != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_mouseHookId);
                    _mouseHookId = IntPtr.Zero;
                }

                _isTracking = false;
                _isTrackingPaused = false;
            }

            AddDebugLog($"✓ Tracking stopped");
        }

        #endregion

        public void Dispose()
        {
            if (_isDisposed) return;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            _isDisposed = true;
            StopTrackingAsync(true).Wait(500);
            GC.SuppressFinalize(this);
        }
    }
}