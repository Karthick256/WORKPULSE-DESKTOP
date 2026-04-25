
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
        private ScreenshotCaptureService _screenshotCaptureService;
        private readonly TokenManager _tokenManager;

        private bool _isTracking;
        private long _currentSessionId;
        private readonly object _lockObject = new object();
        private bool _isDisposed;

        public bool IsDisposed => _isDisposed;

        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_MOUSEWHEEL = 0x020A;


        // Enhanced Mouse counters
        private int _totalLeftClicks;
        private int _totalRightClicks;
        private int _totalMiddleClicks;
        private int _totalDoubleClicks;
        private int _totalScrollEvents;
        private long _totalMouseDistance;
        private Point _lastMousePosition;
        private DateTime _lastScrollTime;
        private DateTime _lastMouseMoveTime;

        // Mouse & Keyboard counters
        private int _totalMouseClicks;
        private int _totalMouseMovements;
        private int _totalKeystrokes;
        private int _totalSpecialKeyCount;
        private int _typingBursts;
        private int _currentBurstKeystrokes;
        private DateTime _lastKeystrokeTime;
        private int _totalActiveTypingSeconds;
        private List<int> _wpmMeasurements = new List<int>();
        private int _peakWpm;
        private readonly object _wpmLock = new object();

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
            _screenshotCaptureService = new ScreenshotCaptureService(trackingService, tokenManager);
            _screenshotCaptureService.StatusChanged += OnScreenshotStatusChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
        }

        private void AddDebugLog(string message)
        {
            var logEntry = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            Debug.WriteLine(logEntry);
            StatusChanged?.Invoke(this, logEntry);
        }

        private void OnScreenshotStatusChanged(object sender, string status)
        {
            AddDebugLog($"[SCREENSHOT] {status}");
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
            if (domain.Contains("linkedin") || domain.Contains("jira") || domain.Contains("confluence") ||
                domain.Contains("notion") || domain.Contains("drive.google") || domain.Contains("docs.google") ||
                domain.Contains("sharepoint") || domain.Contains("office"))
                return UrlCategory.WORK;
            if (domain.Contains("github") || domain.Contains("gitlab") || domain.Contains("bitbucket") ||
                domain.Contains("stackoverflow") || domain.Contains("stackexchange"))
                return UrlCategory.DEVELOPMENT;
            if (domain.Contains("slack") || domain.Contains("teams") || domain.Contains("zoom") ||
                domain.Contains("meet") || domain.Contains("web.whatsapp"))
                return UrlCategory.COMMUNICATION;
            if (domain.Contains("gmail") || domain.Contains("outlook") || domain.Contains("mail"))
                return UrlCategory.EMAIL;
            if (domain.Contains("coursera") || domain.Contains("udemy") || domain.Contains("edx") ||
                domain.Contains("khanacademy") || domain.Contains("geeksforgeeks"))
                return UrlCategory.LEARNING;
            if (domain.Contains("google") || domain.Contains("bing") || domain.Contains("duckduckgo"))
                return UrlCategory.SEARCH;
            if (domain.Contains("bbc") || domain.Contains("cnn") || domain.Contains("ndtv") ||
                domain.Contains("thehindu"))
                return UrlCategory.NEWS;
            if (domain.Contains("facebook") || domain.Contains("instagram") || domain.Contains("twitter") ||
                domain.Contains("snapchat") || domain.Contains("reddit"))
                return UrlCategory.SOCIAL;
            if (domain.Contains("youtube") || domain.Contains("netflix") || domain.Contains("twitch"))
                return UrlCategory.ENTERTAINMENT;
            if (domain.Contains("amazon") || domain.Contains("ebay") || domain.Contains("flipkart"))
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
            if (appName.Contains("excel") || appName.Contains("word") || appName.Contains("powerpoint") ||
                appName.Contains("onenote") || appName.Contains("winword"))
                return AppCategory.OFFICE;
            if (appName.Contains("outlook") || appName.Contains("slack") || appName.Contains("teams") ||
                appName.Contains("zoom") || appName.Contains("skype") || appName.Contains("meet"))
                return AppCategory.COMMUNICATION;
            if (appName.Contains("visual studio") || appName.Contains("code") || appName.Contains("intellij") ||
                appName.Contains("eclipse") || appName.Contains("android studio") || appName.Contains("devenv") ||
                appName.Contains("webstorm") || appName.Contains("monitor_desktop"))
                return AppCategory.DEVELOPMENT;
            if (appName.Contains("mysql") || appName.Contains("postgres") || appName.Contains("mongodb") ||
                appName.Contains("sql server") || appName.Contains("oracle") || appName.Contains("dbeaver"))
                return AppCategory.DATABASE;
            if (appName.Contains("photoshop") || appName.Contains("illustrator") || appName.Contains("figma") ||
                appName.Contains("canva") || appName.Contains("after effects") || appName.Contains("premiere"))
                return AppCategory.DESIGN;
            if (appName.Contains("chrome") || appName.Contains("firefox") || appName.Contains("edge") ||
                appName.Contains("safari") || appName.Contains("opera") || appName.Contains("brave") || appName.Contains("ulaa"))
                return AppCategory.BROWSER;
            if (appName.Contains("task manager") || appName.Contains("settings") || appName.Contains("control panel") ||
                appName.Contains("cmd") || appName.Contains("powershell") || appName.Contains("terminal") || appName.Contains("explorer"))
                return AppCategory.SYSTEM;
            if (appName.Contains("antivirus") || appName.Contains("windows defender") || appName.Contains("kaspersky") ||
                appName.Contains("norton") || appName.Contains("mcafee"))
                return AppCategory.SECURITY;
            if (appName.Contains("spotify") || appName.Contains("netflix") || appName.Contains("youtube") ||
                appName.Contains("vlc") || appName.Contains("prime video"))
                return AppCategory.ENTERTAINMENT;
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

                // Send previous URL visit IMMEDIATELY (like application usage)
                if (!string.IsNullOrEmpty(_currentUrl))
                {
                    var endTime = DateTime.Now;
                    var durationSeconds = (int)(endTime - _currentUrlStartTime).TotalSeconds;

                    if (durationSeconds > 0)
                    {
                        // Send directly to API instead of adding to pending list
                        var urlRequest = new BrowserUrlVisitRequest
                        {
                            SessionId = _currentSessionId,
                            Url = _currentUrl,
                            PageTitle = _currentUrlTitle,
                            Domain = _currentUrlDomain,
                            Category = GetUrlCategory(_currentUrlDomain, _currentUrl),
                            VisitedAt = endTime,
                            TimeSpentSeconds = durationSeconds,
                            IsProductive = true,
                            VisitCount = 1
                        };

                        AddDebugLog($"Sending URL immediately: {_currentUrlDomain} ({durationSeconds}s)");
                        var response = await _trackingService.SaveBrowserUrlVisit(urlRequest);

                        if (response.Status == 200 || response.Status == 201)
                        {
                            AddDebugLog($"✓ URL sent immediately: {_currentUrlDomain}");
                        }
                        else
                        {
                            AddDebugLog($"✗ Failed to send URL: {_currentUrlDomain} - {response.Message}");
                            // If failed, add to pending list for later retry
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
                        }
                    }
                }

                // Start tracking new URL
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

            // Only send pending URLs that failed during immediate send (retry)
            foreach (var visit in _pendingUrlVisits)
            {
                var urlRequest = new BrowserUrlVisitRequest
                {
                    SessionId = _currentSessionId,
                    Url = visit.Url,
                    PageTitle = visit.PageTitle,
                    Domain = visit.Domain,
                    Category = visit.Category,
                    VisitedAt = visit.EndTime,
                    TimeSpentSeconds = visit.DurationSeconds,
                    IsProductive = visit.IsProductive,
                    VisitCount = visit.VisitCount
                };

                AddDebugLog($"Retrying failed URL: {visit.Domain} ({visit.DurationSeconds}s)");
                var response = await _trackingService.SaveBrowserUrlVisit(urlRequest);

                if (response.Status == 200 || response.Status == 201)
                {
                    AddDebugLog($"✓ URL retry successful: {visit.Domain}");
                }
                else
                {
                    AddDebugLog($"✗ URL retry failed: {visit.Domain} - {response.Message}");
                }
            }

            // Send current URL if it exists and hasn't been sent yet
            if (!string.IsNullOrEmpty(_currentUrl))
            {
                var currentDuration = (int)(endTime - _currentUrlStartTime).TotalSeconds;
                if (currentDuration > 0)
                {
                    var currentUrlRequest = new BrowserUrlVisitRequest
                    {
                        SessionId = _currentSessionId,
                        Url = _currentUrl,
                        PageTitle = _currentUrlTitle,
                        Domain = _currentUrlDomain,
                        Category = GetUrlCategory(_currentUrlDomain, _currentUrl),
                        VisitedAt = endTime,
                        TimeSpentSeconds = currentDuration,
                        IsProductive = true,
                        VisitCount = 1
                    };

                    AddDebugLog($"Sending final URL: {_currentUrlDomain} ({currentDuration}s)");
                    var response = await _trackingService.SaveBrowserUrlVisit(currentUrlRequest);

                    if (response.Status == 200 || response.Status == 201)
                    {
                        AddDebugLog($"✓ Final URL sent: {_currentUrlDomain}");
                    }
                    else
                    {
                        AddDebugLog($"✗ Failed to send final URL: {_currentUrlDomain} - {response.Message}");
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

            // Start WPM calculation timer
            Task.Run(async () =>
            {
                while (_isTracking && !_isDisposed)
                {
                    await Task.Delay(60000); // Calculate WPM every minute
                    CalculateWPM();
                }
            });
        }

        private void CalculateWPM()
        {
            if (!_isTracking || _isTrackingPaused) return;

            lock (_wpmLock)
            {
                // Calculate WPM for the last minute (assuming average 5 chars per word)
                var keystrokesInLastMinute = _currentBurstKeystrokes;
                var wpm = (keystrokesInLastMinute / 5);

                if (wpm > 0)
                {
                    _wpmMeasurements.Add(wpm);
                    if (wpm > _peakWpm)
                    {
                        _peakWpm = wpm;
                    }

                    // Count as typing burst if WPM > 20 (meaningful typing)
                    if (wpm > 20)
                    {
                        _typingBursts++;
                    }

                    // Add to active typing seconds if WPM > 0
                    if (wpm > 0)
                    {
                        _totalActiveTypingSeconds += 60;
                    }
                }

                // Reset burst counter for next minute
                _currentBurstKeystrokes = 0;
            }
        }

        private async void SendMouseKeyboardData(object state)
        {
            if (!_isTracking || _currentSessionId == 0 || _isTrackingPaused) return;

            int leftClicks = Interlocked.Exchange(ref _totalLeftClicks, 0);
            int rightClicks = Interlocked.Exchange(ref _totalRightClicks, 0);
            int middleClicks = Interlocked.Exchange(ref _totalMiddleClicks, 0);
            int doubleClicks = Interlocked.Exchange(ref _totalDoubleClicks, 0);
            int scrollEvents = Interlocked.Exchange(ref _totalScrollEvents, 0);
            long distancePixels = Interlocked.Read(ref _totalMouseDistance);
            Interlocked.Exchange(ref _totalMouseDistance, 0);

            int keystrokes = Interlocked.Exchange(ref _totalKeystrokes, 0);
            int specialKeyCount = Interlocked.Exchange(ref _totalSpecialKeyCount, 0);
            int typingBursts = Interlocked.Exchange(ref _typingBursts, 0);
            int activeTypingSeconds = Interlocked.Exchange(ref _totalActiveTypingSeconds, 0);

            int avgWpm = 0;
            int peakWpm = 0;

            lock (_wpmLock)
            {
                if (_wpmMeasurements.Count > 0)
                {
                    avgWpm = (int)_wpmMeasurements.Average();
                }
                peakWpm = _peakWpm;

                // Reset WPM measurements for next interval
                _wpmMeasurements.Clear();
                _peakWpm = 0;
            }

            await SendMouseActivityAsync(leftClicks, rightClicks, middleClicks, doubleClicks, scrollEvents, distancePixels);
            await SendKeyboardActivityAsync(keystrokes, specialKeyCount, typingBursts, avgWpm, peakWpm, activeTypingSeconds);
        }

        private async Task SendMouseActivityAsync(int leftClicks, int rightClicks, int middleClicks, int doubleClicks, int scrollEvents, long distancePixels)
        {
            if (!_isTracking || _currentSessionId == 0) return;

            int totalClicks = leftClicks + rightClicks + middleClicks;
            if (totalClicks == 0 && scrollEvents == 0 && distancePixels == 0) return;

            try
            {
                var request = new MouseActivityRequest
                {
                    SessionId = _currentSessionId,
                    RecordedAt = DateTime.Now,
                    IntervalSeconds = _mouseKeyboardSendIntervalMinutes * 60,
                    TotalClicks = totalClicks,
                    LeftClicks = leftClicks,
                    RightClicks = rightClicks,
                    MiddleClicks = middleClicks,
                    DoubleClicks = doubleClicks,
                    ScrollEvents = scrollEvents,
                    DistancePixels = distancePixels
                };

                var response = await _trackingService.SaveMouseActivity(request);
                if (response.Status == 200 || response.Status == 201)
                    AddDebugLog($"✓ Mouse sent: L:{leftClicks} R:{rightClicks} M:{middleClicks} D:{doubleClicks} Scr:{scrollEvents} Dist:{distancePixels}px");
            }
            catch (Exception ex)
            {
                AddDebugLog($"✗ Error sending mouse activity: {ex.Message}");
            }
        }

        private async Task SendKeyboardActivityAsync(int keystrokes, int specialKeyCount, int typingBursts, int avgWpm, int peakWpm, int activeTypingSeconds)
        {
            if (!_isTracking || _currentSessionId == 0) return;
            if (keystrokes == 0 && specialKeyCount == 0) return;

            try
            {
                var request = new KeyboardActivityRequest
                {
                    SessionId = _currentSessionId,
                    RecordedAt = DateTime.Now,
                    IntervalSeconds = _mouseKeyboardSendIntervalMinutes * 60,
                    TotalKeystrokes = keystrokes,
                    SpecialKeyCount = specialKeyCount,
                    TypingBursts = typingBursts,
                    AvgWpm = avgWpm,
                    PeakWpm = peakWpm,
                    ActiveTypingSeconds = activeTypingSeconds
                };

                var response = await _trackingService.SaveKeyboardActivity(request);
                if (response.Status == 200 || response.Status == 201)
                    AddDebugLog($"✓ Keyboard sent: K:{keystrokes} Special:{specialKeyCount} Bursts:{typingBursts} WPM:{avgWpm}/{peakWpm} Active:{activeTypingSeconds}s");
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
                if (!_isTrackingPaused)
                {
                    // Get the key information
                    int vkCode = Marshal.ReadInt32(lParam);

                    // Check if it's a special key
                    if (IsSpecialKey(vkCode))
                    {
                        Interlocked.Increment(ref _totalSpecialKeyCount);
                    }

                    Interlocked.Increment(ref _totalKeystrokes);
                    Interlocked.Increment(ref _currentBurstKeystrokes);

                    // Update last keystroke time for burst detection
                    _lastKeystrokeTime = DateTime.Now;
                }
                ResetIdleState();
            }
            return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        private bool IsSpecialKey(int vkCode)
        {
            // Define special keys (modifiers, navigation, function keys, etc.)
            HashSet<int> specialKeys = new HashSet<int>
            {
                // Modifiers
                0x10, // Shift
                0x11, // Ctrl
                0x12, // Alt
                0x5B, // Left Windows
                0x5C, // Right Windows
                
                // Navigation
                0x21, // Page Up
                0x22, // Page Down
                0x23, // End
                0x24, // Home
                0x25, // Left Arrow
                0x26, // Up Arrow
                0x27, // Right Arrow
                0x28, // Down Arrow
                
                // Editing
                0x2E, // Delete
                0x08, // Backspace
                0x0D, // Enter
                0x1B, // Escape
                0x09, // Tab
                
                // Function keys
                0x70, // F1
                0x71, // F2
                0x72, // F3
                0x73, // F4
                0x74, // F5
                0x75, // F6
                0x76, // F7
                0x77, // F8
                0x78, // F9
                0x79, // F10
                0x7A, // F11
                0x7B, // F12
                
                // Other
                0x14, // Caps Lock
                0x90, // Num Lock
                0x91, // Scroll Lock
                0x2C, // Print Screen
                0x13, // Pause
                
                // Media keys (common codes)
                0xAE, // Volume down
                0xAF, // Volume up
                0xAD, // Mute
                0xB3, // Next track
                0xB1, // Previous track
                0xB0, // Play/Pause
            };

            return specialKeys.Contains(vkCode);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                if (!_isTrackingPaused)
                {
                    switch ((int)wParam)
                    {
                        case WM_LBUTTONDOWN:
                            Interlocked.Increment(ref _totalLeftClicks);
                            ResetIdleState();
                            break;

                        case WM_RBUTTONDOWN:
                            Interlocked.Increment(ref _totalRightClicks);
                            ResetIdleState();
                            break;

                        case WM_MBUTTONDOWN:
                            Interlocked.Increment(ref _totalMiddleClicks);
                            ResetIdleState();
                            break;

                        case WM_LBUTTONDBLCLK:
                            Interlocked.Increment(ref _totalDoubleClicks);
                            ResetIdleState();
                            break;

                        case WM_MOUSEWHEEL:
                            Interlocked.Increment(ref _totalScrollEvents);
                            _lastScrollTime = DateTime.Now;
                            ResetIdleState();
                            break;

                        case WM_MOUSEMOVE:
                            // Calculate mouse movement distance
                            if (_lastMousePosition.X != 0 && _lastMousePosition.Y != 0)
                            {
                                MSLLHOOKSTRUCT hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                                Point currentPos = new Point(hookStruct.pt.x, hookStruct.pt.y);
                                double distance = Math.Sqrt(
                                    Math.Pow(currentPos.X - _lastMousePosition.X, 2) +
                                    Math.Pow(currentPos.Y - _lastMousePosition.Y, 2)
                                );
                                Interlocked.Add(ref _totalMouseDistance, (long)distance);
                                _lastMousePosition = currentPos;
                            }
                            else
                            {
                                MSLLHOOKSTRUCT hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                                _lastMousePosition = new Point(hookStruct.pt.x, hookStruct.pt.y);
                            }
                            ResetIdleState();
                            break;
                    }
                }
            }
            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
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

                // Reset mouse counters
                _totalLeftClicks = 0;
                _totalRightClicks = 0;
                _totalMiddleClicks = 0;
                _totalDoubleClicks = 0;
                _totalScrollEvents = 0;
                _totalMouseDistance = 0;
                _lastMousePosition = new Point(0, 0);

                // Reset keyboard counters
                _totalKeystrokes = 0;
                _totalSpecialKeyCount = 0;
                _typingBursts = 0;
                _currentBurstKeystrokes = 0;
                _totalActiveTypingSeconds = 0;
                _wpmMeasurements.Clear();
                _peakWpm = 0;

                _currentAppName = null;
                _currentBrowserName = null;
                _currentBrowserTempId = null;
                _currentUrl = null;
                _isBrowserActive = false;
                _pendingUrlVisits.Clear();

                _screenshotCaptureService?.StartPolling(sessionId);
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
            _screenshotCaptureService?.StopPolling();
            if (!_isTracking) return;

            if (sendFinalData && !_isTrackingPaused)
            {
                // Send current application
                if (!string.IsNullOrEmpty(_currentAppName))
                    SendApplicationUsageAndReset();

                // Send browser usage and any pending/final URLs
                if (_isBrowserActive)
                    await SaveCurrentBrowserAndPendingUrls();

                // Send final mouse data with all counters
                int leftClicks = Interlocked.Exchange(ref _totalLeftClicks, 0);
                int rightClicks = Interlocked.Exchange(ref _totalRightClicks, 0);
                int middleClicks = Interlocked.Exchange(ref _totalMiddleClicks, 0);
                int doubleClicks = Interlocked.Exchange(ref _totalDoubleClicks, 0);
                int scrollEvents = Interlocked.Exchange(ref _totalScrollEvents, 0);
                long distancePixels = Interlocked.Read(ref _totalMouseDistance);
                Interlocked.Exchange(ref _totalMouseDistance, 0);

                // Send final keyboard data with all counters
                int keystrokes = Interlocked.Exchange(ref _totalKeystrokes, 0);
                int specialKeyCount = Interlocked.Exchange(ref _totalSpecialKeyCount, 0);
                int typingBursts = Interlocked.Exchange(ref _typingBursts, 0);
                int activeTypingSeconds = Interlocked.Exchange(ref _totalActiveTypingSeconds, 0);

                int avgWpm = 0;
                int peakWpm = 0;

                lock (_wpmLock)
                {
                    if (_wpmMeasurements.Count > 0)
                    {
                        avgWpm = (int)_wpmMeasurements.Average();
                    }
                    peakWpm = _peakWpm;
                }

                await SendMouseActivityAsync(leftClicks, rightClicks, middleClicks, doubleClicks, scrollEvents, distancePixels);
                await SendKeyboardActivityAsync(keystrokes, specialKeyCount, typingBursts, avgWpm, peakWpm, activeTypingSeconds);
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
            _screenshotCaptureService?.Dispose();
            _screenshotCaptureService = null;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            _isDisposed = true;
            StopTrackingAsync(true).Wait(500);
            GC.SuppressFinalize(this);
        }
    }
}