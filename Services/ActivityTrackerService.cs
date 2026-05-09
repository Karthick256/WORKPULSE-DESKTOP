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
        private LiveScreenStreamingService _liveScreenService;
        private readonly TokenManager _tokenManager;

        private bool _isTracking;
        private long _currentSessionId;
        private readonly object _lockObject = new object();
        private bool _isDisposed;

        public bool IsDisposed => _isDisposed;

        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_MOUSEWHEEL = 0x020A;

        private int _totalLeftClicks;
        private int _totalRightClicks;
        private int _totalMiddleClicks;
        private int _totalDoubleClicks;
        private int _totalScrollEvents;
        private long _totalMouseDistance;
        private Point _lastMousePosition;
        private DateTime _lastScrollTime;
        private DateTime _lastMouseMoveTime;

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

        private DateTime _lastActivityTime = DateTime.Now;
        private bool _isIdle;
        private DateTime _idleStartTime;
        private Timer _idleCheckTimer;
        private bool _isTrackingPaused;

        private long? _currentBreakId;
        private DateTime? _currentBreakStartTime;
        private BreakType _currentBreakType;
        private bool _isOnBreak;
        private readonly object _breakLock = new object();

        private string _currentAppName;
        private string _currentWindowTitle;
        private DateTime _currentAppStartTime;
        private int _currentAppFocusCount;
        private bool _isAppTrackingPaused;

        private bool _isBrowserActive;
        private string _currentBrowserName;
        private DateTime _currentBrowserStartTime;
        private long? _currentBrowserTempId;
        private bool _isBrowserTrackingPaused;

        private string _currentUrl;
        private string _currentUrlTitle;
        private string _currentUrlDomain;
        private DateTime _currentUrlStartTime;
        private List<PendingUrlVisit> _pendingUrlVisits = new List<PendingUrlVisit>();
        private bool _isUrlTrackingPaused;

        // Browser URL tracking cache
        private Dictionary<string, string> _urlCache = new Dictionary<string, string>();
        private DateTime _lastUrlCheckTime = DateTime.MinValue;
        private readonly TimeSpan _urlCheckThrottle = TimeSpan.FromSeconds(2);

        private LowLevelKeyboardProc _keyboardProc;
        private LowLevelMouseProc _mouseProc;
        private IntPtr _keyboardHookId = IntPtr.Zero;
        private IntPtr _mouseHookId = IntPtr.Zero;

        private int _idleThresholdSeconds = 120;
        private bool _isSystemSleeping;

        public bool IsOnBreak => _isOnBreak;
        public long? CurrentBreakId => _currentBreakId;
        public DateTime? CurrentBreakStartTime => _currentBreakStartTime;

        private Timer _heartbeatTimer;
        private Timer _browserUrlCheckTimer;

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

        private void OnScreenshotStatusChanged(object sender, string status)
        {
            StatusChanged?.Invoke(this, $"[SCREENSHOT] {status}");
        }

        private void OnLiveScreenStatusChanged(object sender, string status)
        {
            StatusChanged?.Invoke(this, $"[LIVE-STREAM] {status}");
        }

        private void OnLiveScreenError(object sender, string error)
        {
            StatusChanged?.Invoke(this, $"[LIVE-STREAM] ERROR: {error}");
        }

        private void OnLiveScreenStreamingStatusChanged(object sender, bool isStreaming)
        {
            StatusChanged?.Invoke(this, isStreaming ? "[LIVE-STREAM] STREAMING ACTIVE" : "[LIVE-STREAM] STREAMING STOPPED");
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

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
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

        private const uint GW_HWNDNEXT = 2;
        private const uint GW_CHILD = 5;

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
                    PauseTrackingForIdle();
                    break;
                case PowerModes.Resume:
                    _isSystemSleeping = false;
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
                    PauseTrackingForIdle();
                    break;
                case SessionSwitchReason.SessionUnlock:
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
                    _ = SendIdlePeriodAsync(_idleStartTime, idleEndTime, idleDurationSeconds);
                }

                _isIdle = false;
                ResumeTrackingAfterIdle();
            }
        }

        private void CheckIdleState()
        {
            if (_isOnBreak)
            {
                if (_isIdle)
                {
                    _isIdle = false;
                    ResumeTrackingAfterIdle();
                }
                return;
            }
            uint systemIdleTimeMs = GetIdleTime();
            double systemIdleSeconds = systemIdleTimeMs / 1000.0;
            var manualIdleTime = DateTime.Now - _lastActivityTime;
            double effectiveIdleSeconds = Math.Max(systemIdleSeconds, manualIdleTime.TotalSeconds);
            if (!_isIdle && effectiveIdleSeconds >= _idleThresholdSeconds)
            {
                _isIdle = true;
                _idleStartTime = DateTime.Now;
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

                await _trackingService.LogIdlePeriod(request);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sending idle period: {ex.Message}");
            }
        }
        #endregion

        public async Task<bool> StartBreakAsync(BreakType breakType, string notes = null)
        {
            if (!_isTracking || _currentSessionId == 0)
            {
                StatusChanged?.Invoke(this, "[BREAK] Error: No active session");
                return false;
            }

            if (_isOnBreak)
            {
                StatusChanged?.Invoke(this, "[BREAK] Already on break");
                return false;
            }

            lock (_breakLock)
            {
                _isTrackingPaused = true;
                _isAppTrackingPaused = true;
                _isBrowserTrackingPaused = true;
                _isUrlTrackingPaused = true;
                _isOnBreak = true;
                _currentBreakStartTime = DateTime.Now;
                _currentBreakType = breakType;
            }

            try
            {
                var request = new BreakStartRequest
                {
                    SessionId = _currentSessionId,
                    BreakType = breakType,
                    TriggerReason = BreakTrigger.MANUAL,
                    Notes = notes ?? string.Empty,
                    IsPlanned = true
                };

                var response = await _trackingService.StartBreak(request);

                if (response.Status == 200 && response.Data != null)
                {
                    _currentBreakId = response.Data.Id;
                    StatusChanged?.Invoke(this, $"[BREAK] Started {GetBreakTypeName(breakType)} break");
                    return true;
                }
                else
                {
                    // Rollback on failure
                    lock (_breakLock)
                    {
                        _isTrackingPaused = false;
                        _isAppTrackingPaused = false;
                        _isBrowserTrackingPaused = false;
                        _isUrlTrackingPaused = false;
                        _isOnBreak = false;
                        _currentBreakStartTime = null;
                        _currentBreakId = null;
                    }
                    StatusChanged?.Invoke(this, $"[BREAK] Failed to start break: {response.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                lock (_breakLock)
                {
                    _isTrackingPaused = false;
                    _isAppTrackingPaused = false;
                    _isBrowserTrackingPaused = false;
                    _isUrlTrackingPaused = false;
                    _isOnBreak = false;
                    _currentBreakStartTime = null;
                    _currentBreakId = null;
                }
                StatusChanged?.Invoke(this, $"[BREAK] Error starting break: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> EndBreakAsync()
        {
            if (!_isTracking || _currentSessionId == 0)
            {
                StatusChanged?.Invoke(this, "[BREAK] Error: No active session");
                return false;
            }

            if (!_isOnBreak || !_currentBreakId.HasValue)
            {
                var activeBreak = await _trackingService.GetMyActiveBreak();
                if (activeBreak?.Status == 200 && activeBreak.Data != null)
                {
                    _currentBreakId = activeBreak.Data.Id;
                    _currentBreakStartTime = activeBreak.Data.BreakStart;
                    _currentBreakType = activeBreak.Data.BreakType;

                    lock (_breakLock)
                    {
                        _isTrackingPaused = true;
                        _isAppTrackingPaused = true;
                        _isBrowserTrackingPaused = true;
                        _isUrlTrackingPaused = true;
                        _isOnBreak = true;
                    }
                    StatusChanged?.Invoke(this, $"[BREAK] Restored break state from server");
                }
                else
                {
                    return false;
                }
            }

            try
            {
                var response = await _trackingService.EndMyBreak();

                if (response.Status == 200)
                {
                    var breakDuration = (int)(DateTime.Now - _currentBreakStartTime.Value).TotalSeconds;
                    StatusChanged?.Invoke(this, $"[BREAK] Ended break after {FormatBreakDuration(breakDuration)}");
                    lock (_breakLock)
                    {
                        _isTrackingPaused = false;
                        _isAppTrackingPaused = false;
                        _isBrowserTrackingPaused = false;
                        _isUrlTrackingPaused = false;
                        _isOnBreak = false;
                        _currentBreakId = null;
                        _currentBreakStartTime = null;
                    }

                    _lastActivityTime = DateTime.Now;

                    return true;
                }
                else
                {
                    StatusChanged?.Invoke(this, $"[BREAK] Failed to end break: {response.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"[BREAK] Error ending break: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CheckAndRestoreBreakState()
        {
            if (!_isTracking || _currentSessionId == 0) return false;

            try
            {
                var response = await _trackingService.GetMyActiveBreak();

                if (response.Status == 200 && response.Data != null)
                {
                    var activeBreak = response.Data;

                    if (!_isOnBreak)
                    {
                        lock (_breakLock)
                        {
                            _isTrackingPaused = true;
                            _isAppTrackingPaused = true;
                            _isBrowserTrackingPaused = true;
                            _isUrlTrackingPaused = true;
                            _isOnBreak = true;
                            _currentBreakId = activeBreak.Id;
                            _currentBreakStartTime = activeBreak.BreakStart;
                            _currentBreakType = activeBreak.BreakType;
                        }
                        StatusChanged?.Invoke(this, $"[BREAK] Restored break state: {GetBreakTypeName(_currentBreakType)}");
                        return true;
                    }
                    else if (_currentBreakId != activeBreak.Id)
                    {
                        // Sync break ID if different
                        _currentBreakId = activeBreak.Id;
                        _currentBreakStartTime = activeBreak.BreakStart;
                        _currentBreakType = activeBreak.BreakType;
                        return true;
                    }
                    return true;
                }
                else if (_isOnBreak)
                {
                    // Local says on break but server doesn't - sync state
                    lock (_breakLock)
                    {
                        _isTrackingPaused = false;
                        _isAppTrackingPaused = false;
                        _isBrowserTrackingPaused = false;
                        _isUrlTrackingPaused = false;
                        _isOnBreak = false;
                        _currentBreakId = null;
                        _currentBreakStartTime = null;
                    }
                    StatusChanged?.Invoke(this, "[BREAK] Break state synchronized with server");
                    return false;
                }

                return _isOnBreak;
            }
            catch (Exception ex)
            {
                return _isOnBreak;
            }
        }

        private string GetBreakTypeName(BreakType type)
        {
            return type switch
            {
                BreakType.LUNCH => "Lunch",
                BreakType.SHORT_BREAK => "Short",
                BreakType.LONG_BREAK => "Long",
                BreakType.MEETING => "Meeting",
                BreakType.TRAINING => "Training",
                BreakType.PERSONAL => "Personal",
                _ => "Break"
            };
        }

        private string FormatBreakDuration(int seconds)
        {
            var minutes = seconds / 60;
            var remainingSeconds = seconds % 60;
            return minutes > 0 ? $"{minutes}m {remainingSeconds}s" : $"{seconds}s";
        }

        #region Browser URL Extraction - ENHANCED
        private bool IsBrowserProcess(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return false;

            processName = processName.ToLower();
            var browsers = new[] {
                "chrome",
                "firefox",
                "edge",
                "msedge",
                "opera",
                "brave",
                "chromium",
                "browser",
                "iexplore"
            };

            return browsers.Any(b => processName.Contains(b));
        }

        private string GetBrowserNameFromProcess(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return "Unknown";

            processName = processName.ToLower();
            if (processName.Contains("chrome")) return "Chrome";
            if (processName.Contains("firefox")) return "Firefox";
            if (processName.Contains("edge") || processName.Contains("msedge")) return "Edge";
            if (processName.Contains("opera")) return "Opera";
            if (processName.Contains("brave")) return "Brave";
            if (processName.Contains("chromium")) return "Chromium";
            if (processName.Contains("iexplore")) return "Internet Explorer";

            return processName;
        }

        private string GetBrowserUrl(IntPtr hWnd, string browserType)
        {
            if (hWnd == IntPtr.Zero || string.IsNullOrEmpty(browserType))
                return null;
            string cacheKey = $"{hWnd}_{browserType}";
            if (_urlCache.ContainsKey(cacheKey))
            {
                var cachedUrl = _urlCache[cacheKey];
                if ((DateTime.Now - _lastUrlCheckTime).TotalSeconds < 5)
                {
                    return cachedUrl;
                }
            }

            try
            {
                string url = null;
                if (browserType.Contains("chrome") || browserType.Contains("edge") ||
                    browserType.Contains("chromium") || browserType.Contains("brave"))
                {
                    url = GetChromiumUrl(hWnd);
                }
                else if (browserType.Contains("firefox"))
                {
                    url = GetFirefoxUrl(hWnd);
                }

                if (string.IsNullOrEmpty(url))
                {
                    url = GetGenericBrowserUrl(hWnd);
                }

                if (string.IsNullOrEmpty(url))
                {
                    var title = GetWindowTitle(hWnd);
                    url = ExtractUrlFromTitle(title);
                }

                if (!string.IsNullOrEmpty(url))
                {
                    _urlCache[cacheKey] = url;
                    _lastUrlCheckTime = DateTime.Now;
                }
                else
                {
                    Debug.WriteLine($"[URL TRACKING] No URL found for {browserType} with handle {hWnd}");
                }

                return url;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        private string GetChromiumUrl(IntPtr hWnd)
        {
            try
            {
                var element = AutomationElement.FromHandle(hWnd);
                if (element == null)
                {
                    return null;
                }

                var url = GetUrlFromEditControl(element);
                if (!string.IsNullOrEmpty(url)) return url;

                url = GetUrlFromChildWindows(hWnd);
                if (!string.IsNullOrEmpty(url)) return url;
       
                url = GetUrlFromAnyTextElement(element);
                if (!string.IsNullOrEmpty(url)) return url;

                return null;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        private string GetUrlFromEditControl(AutomationElement parentElement)
        {
            try
            {
                var editControls = parentElement.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

                foreach (AutomationElement editControl in editControls)
                {
                    try
                    {
                        if (editControl.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePatternObj))
                        {
                            var valuePattern = valuePatternObj as ValuePattern;
                            if (valuePattern != null)
                            {
                                var text = valuePattern.Current.Value;
                                if (!string.IsNullOrEmpty(text) && IsValidUrl(text))
                                {
                                    return text;
                                }
                            }
                        }
                        if (editControl.TryGetCurrentPattern(TextPattern.Pattern, out object textPatternObj))
                        {
                            var textPattern = textPatternObj as TextPattern;
                            if (textPattern != null)
                            {
                                var text = textPattern.DocumentRange.GetText(-1);
                                if (!string.IsNullOrEmpty(text))
                                {
                                    var url = ExtractUrlFromText(text);
                                    if (!string.IsNullOrEmpty(url))
                                    {
                                        return url;
                                    }
                                }
                            }
                        }
                        var name = editControl.Current.Name;
                        if (!string.IsNullOrEmpty(name) && IsValidUrl(name))
                        {
                            return name;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[URL TRACKING] Error reading edit control: {ex.Message}");
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        private string GetUrlFromChildWindows(IntPtr parentHwnd)
        {
            try
            {
                string foundUrl = null;
                var childWindows = new List<IntPtr>();
                EnumChildWindows(parentHwnd, (hWnd, lParam) =>
                {
                    if (IsWindowVisible(hWnd))
                    {
                        childWindows.Add(hWnd);
                    }
                    return true;
                }, IntPtr.Zero);
                foreach (var childHwnd in childWindows)
                {
                    try
                    {
                        var element = AutomationElement.FromHandle(childHwnd);
                        if (element != null)
                        {
                            var url = GetUrlFromEditControl(element);
                            if (!string.IsNullOrEmpty(url))
                            {
                                foundUrl = url;
                                break;
                            }
                        }
                    }
                    catch { }
                }

                return foundUrl;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        private string GetUrlFromAnyTextElement(AutomationElement parentElement)
        {
            try
            {
                var allElements = parentElement.FindAll(
                    TreeScope.Descendants,
                    System.Windows.Automation.Condition.TrueCondition);

                foreach (AutomationElement element in allElements)
                {
                    try
                    {
                        if (element.TryGetCurrentPattern(TextPattern.Pattern, out object textPatternObj))
                        {
                            var textPattern = textPatternObj as TextPattern;
                            if (textPattern != null)
                            {
                                var text = textPattern.DocumentRange.GetText(-1);
                                if (!string.IsNullOrEmpty(text))
                                {
                                    var url = ExtractUrlFromText(text);
                                    if (!string.IsNullOrEmpty(url))
                                    {
                                        return url;
                                    }
                                }
                            }
                        }
                        var name = element.Current.Name;
                        if (!string.IsNullOrEmpty(name))
                        {
                            var url = ExtractUrlFromText(name);
                            if (!string.IsNullOrEmpty(url))
                            {
                                return url;
                            }
                        }
                    }
                    catch { }
                }

                return null;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        private string GetGenericBrowserUrl(IntPtr hWnd)
        {
            try
            {
                var element = AutomationElement.FromHandle(hWnd);
                if (element == null) return null;
                var availablePatterns = element.GetSupportedPatterns();
                foreach (var pattern in availablePatterns)
                {
                    try
                    {
                        if (pattern.Id == ValuePattern.Pattern.Id)
                        {
                            var valuePattern = element.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
                            var value = valuePattern?.Current.Value;
                            if (!string.IsNullOrEmpty(value) && IsValidUrl(value))
                                return value;
                        }
                    }
                    catch { }
                }
                var title = element.Current.Name;
                if (!string.IsNullOrEmpty(title))
                {
                    var url = ExtractUrlFromText(title);
                    if (!string.IsNullOrEmpty(url))
                        return url;
                }

                return null;
            }
            catch (Exception ex)
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
                var conditions = new List<System.Windows.Automation.Condition>
                {
                    new AndCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                        new PropertyCondition(AutomationElement.NameProperty, "Search or enter address")
                    ),
                    new AndCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                        new PropertyCondition(AutomationElement.NameProperty, "Address Bar")
                    ),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)
                };

                foreach (var condition in conditions)
                {
                    var addressBar = element.FindFirst(TreeScope.Descendants, condition);
                    if (addressBar == null)
                    {
                        addressBar = element.FindFirst(TreeScope.Children, condition);
                    }

                    if (addressBar != null)
                    {
                        if (addressBar.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePatternObj))
                        {
                            var valuePattern = valuePatternObj as ValuePattern;
                            if (valuePattern != null)
                            {
                                var url = valuePattern.Current.Value;
                                if (!string.IsNullOrEmpty(url) && IsValidUrl(url))
                                {
                                    return url;
                                }
                            }
                        }
                    }
                }
                return GetUrlFromEditControl(element) ?? GetUrlFromChildWindows(hWnd);
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        private string GetWindowTitle(IntPtr hWnd)
        {
            try
            {
                const int nChars = 256;
                StringBuilder buff = new StringBuilder(nChars);
                if (GetWindowText(hWnd, buff, nChars) > 0)
                {
                    return buff.ToString();
                }
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string ExtractUrlFromText(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var urlPattern = @"(https?://[^\s]+)";
            var match = System.Text.RegularExpressions.Regex.Match(text, urlPattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (match.Success)
            {
                return match.Groups[1].Value.TrimEnd('/', ' ', ')', ']');
            }
            var wwwPattern = @"(www\.[^\s]+\.[^\s]+)";
            match = System.Text.RegularExpressions.Regex.Match(text, wwwPattern);

            if (match.Success)
            {
                return "https://" + match.Groups[1].Value.TrimEnd('/', ' ', ')', ']');
            }

            return null;
        }

        private bool IsValidUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                   url.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ||
                   url.Contains("://") ||
                   (url.Contains(".") && !url.Contains(" ") && url.Length > 5);
        }

        private string ExtractDomain(string url)
        {
            try
            {
                if (string.IsNullOrEmpty(url)) return "unknown";
                if (!url.StartsWith("http"))
                {
                    url = "https://" + url;
                }

                var uri = new Uri(url);
                return uri.Host.Replace("www.", "");
            }
            catch
            {
                var domain = url;
                if (domain.Contains("://"))
                    domain = domain.Split(new[] { "://" }, StringSplitOptions.None)[1];
                if (domain.Contains("/"))
                    domain = domain.Split('/')[0];
                return domain.Replace("www.", "");
            }
        }

        private UrlCategory GetUrlCategory(string domain, string url)
        {
            if (string.IsNullOrEmpty(domain)) return UrlCategory.OTHER;

            domain = domain.ToLower();

            if (domain.Contains("linkedin") || domain.Contains("jira") || domain.Contains("confluence") ||
                domain.Contains("notion") || domain.Contains("drive.google") || domain.Contains("docs.google") ||
                domain.Contains("sharepoint") || domain.Contains("office") || domain.Contains("microsoft365") || domain.Contains("teams"))
                return UrlCategory.WORK;
            if (domain.Contains("gmail") || domain.Contains("outlook") || domain.Contains("mail") ||
                domain.Contains("yahoo.") || domain.Contains("protonmail"))
                return UrlCategory.EMAIL;
            if (domain.Contains("coursera") || domain.Contains("udemy") || domain.Contains("edx") ||
                domain.Contains("khanacademy") || domain.Contains("geeksforgeeks") || domain.Contains("w3schools"))
                return UrlCategory.LEARNING;
            if (domain.Contains("bbc") || domain.Contains("cnn") || domain.Contains("ndtv") ||
                domain.Contains("thehindu") || domain.Contains("timesofindia"))
                return UrlCategory.NEWS;
            if (domain.Contains("youtube") || domain.Contains("netflix") || domain.Contains("twitch") ||
                domain.Contains("spotify") || domain.Contains("prime"))
                return UrlCategory.ENTERTAINMENT;
            if (domain.Contains("amazon") || domain.Contains("ebay") || domain.Contains("flipkart") ||
                domain.Contains("shopify") || domain.Contains("etsy"))
                return UrlCategory.SHOPPING;

            return UrlCategory.OTHER;
        }

        private string ExtractUrlFromTitle(string windowTitle)
        {
            if (string.IsNullOrEmpty(windowTitle)) return null;
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

                    endIndex = url.IndexOf(' ');
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

                string windowTitle = GetWindowTitle(handle);
                GetWindowThreadProcessId(handle, out uint processId);
                string processName = "Unknown";

                try
                {
                    var process = Process.GetProcessById((int)processId);
                    processName = process.ProcessName;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting process name: {ex.Message}");
                }

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

                await _trackingService.SaveApplicationUsage(request);
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

        #region Browser and URL Tracking - ENHANCED
        private async void TrackBrowserActivity(IntPtr handle, string processName, string windowTitle)
        {
            if (_isBrowserTrackingPaused)
            {
                return;
            }

            string browserName = GetBrowserNameFromProcess(processName);

            string currentUrl = GetBrowserUrl(handle, processName);

            if (string.IsNullOrEmpty(currentUrl))
            {
                currentUrl = ExtractUrlFromTitle(windowTitle);

                if (string.IsNullOrEmpty(currentUrl))
                {
                    Debug.WriteLine($"[BROWSER TRACKING] No URL found for {browserName} window");
                }
            }

            string domain = !string.IsNullOrEmpty(currentUrl) ? ExtractDomain(currentUrl) : null;

            if (!_isBrowserActive || _currentBrowserName != browserName)
            {
                if (_isBrowserActive)
                {
                    await SaveCurrentBrowserAndPendingUrls();
                }
                _isBrowserActive = true;
                _currentBrowserName = browserName;
                _currentBrowserStartTime = DateTime.Now;
                _currentBrowserTempId = DateTime.Now.Ticks;
                _pendingUrlVisits.Clear();
                _urlCache.Clear();
            }

            if (!_isUrlTrackingPaused && !string.IsNullOrEmpty(currentUrl) && _currentUrl != currentUrl)
            {

                if (!string.IsNullOrEmpty(_currentUrl))
                {
                    var endTime = DateTime.Now;
                    var durationSeconds = (int)(endTime - _currentUrlStartTime).TotalSeconds;

                    if (durationSeconds > 0)
                    {
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

                        try
                        {
                            var response = await _trackingService.SaveBrowserUrlVisit(urlRequest);

                            if (response.Status != 200 && response.Status != 201)
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
                            }
                            else
                            {
                                Debug.WriteLine($"[URL TRACKING] Successfully saved URL visit: {_currentUrl}");
                            }
                        }
                        catch (Exception ex)
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
                        }
                    }
                }

                _currentUrl = currentUrl;
                _currentUrlTitle = windowTitle;
                _currentUrlDomain = domain;
                _currentUrlStartTime = DateTime.Now;
            }
        }

        private async Task SaveCurrentBrowserAndPendingUrls()
        {
            if (!_isBrowserActive || string.IsNullOrEmpty(_currentBrowserName))
            {
                return;
            }

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

                try
                {
                    await _trackingService.SaveBrowserUsage(browserRequest);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BROWSER TRACKING ERROR] Failed to save browser usage: {ex.Message}");
                }
            }

            foreach (var visit in _pendingUrlVisits)
            {
                try
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
                    await _trackingService.SaveBrowserUrlVisit(urlRequest);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[URL TRACKING ERROR] Failed to save pending URL {visit.Url}: {ex.Message}");
                }
            }

            if (!string.IsNullOrEmpty(_currentUrl))
            {
                var currentDuration = (int)(endTime - _currentUrlStartTime).TotalSeconds;
                if (currentDuration > 0)
                {
                    try
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

                        await _trackingService.SaveBrowserUrlVisit(currentUrlRequest);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[URL TRACKING ERROR] Failed to save current URL {_currentUrl}: {ex.Message}");
                    }
                }
            }

            _isBrowserActive = false;
            _currentBrowserName = null;
            _currentBrowserTempId = null;
            _currentUrl = null;
            _currentUrlDomain = null;
            _pendingUrlVisits.Clear();
            _urlCache.Clear();
        }
        #endregion

        #region Mouse & Keyboard
        private void StartMouseKeyboardTimer()
        {
            _mouseKeyboardTimer = new Timer(SendMouseKeyboardData, null,
                TimeSpan.FromMinutes(_mouseKeyboardSendIntervalMinutes),
                TimeSpan.FromMinutes(_mouseKeyboardSendIntervalMinutes));

            Task.Run(async () =>
            {
                while (_isTracking && !_isDisposed)
                {
                    await Task.Delay(60000);
                    CalculateWPM();
                }
            });
        }

        private void CalculateWPM()
        {
            if (!_isTracking || _isTrackingPaused) return;

            lock (_wpmLock)
            {
                var keystrokesInLastMinute = _currentBurstKeystrokes;
                var wpm = (keystrokesInLastMinute / 5);

                if (wpm > 0)
                {
                    _wpmMeasurements.Add(wpm);
                    if (wpm > _peakWpm)
                    {
                        _peakWpm = wpm;
                    }

                    if (wpm > 20)
                    {
                        _typingBursts++;
                    }

                    if (wpm > 0)
                    {
                        _totalActiveTypingSeconds += 60;
                    }
                }

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

                await _trackingService.SaveMouseActivity(request);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sending mouse activity: {ex.Message}");
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

                await _trackingService.SaveKeyboardActivity(request);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sending keyboard activity: {ex.Message}");
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
                    int vkCode = Marshal.ReadInt32(lParam);

                    if (IsSpecialKey(vkCode))
                    {
                        Interlocked.Increment(ref _totalSpecialKeyCount);
                    }

                    Interlocked.Increment(ref _totalKeystrokes);
                    Interlocked.Increment(ref _currentBurstKeystrokes);

                    _lastKeystrokeTime = DateTime.Now;
                }
                ResetIdleState();
            }
            return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        private bool IsSpecialKey(int vkCode)
        {
            HashSet<int> specialKeys = new HashSet<int>
            {
                0x10, 0x11, 0x12, 0x5B, 0x5C,
                0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28,
                0x2E, 0x08, 0x0D, 0x1B, 0x09,
                0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x7B,
                0x14, 0x90, 0x91, 0x2C, 0x13,
                0xAE, 0xAF, 0xAD, 0xB3, 0xB1, 0xB0,
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

                _totalLeftClicks = 0;
                _totalRightClicks = 0;
                _totalMiddleClicks = 0;
                _totalDoubleClicks = 0;
                _totalScrollEvents = 0;
                _totalMouseDistance = 0;
                _lastMousePosition = new Point(0, 0);

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
                _urlCache.Clear();
                _screenshotCaptureService?.StartPolling(sessionId);
                if (_liveScreenService == null)
                {
                    _liveScreenService = new LiveScreenStreamingService(
                        ApiConfig.BaseUrl,
                        _currentSessionId,
                        _tokenManager,
                        _trackingService);

                    _liveScreenService.StatusChanged += OnLiveScreenStatusChanged;
                    _liveScreenService.ErrorOccurred += OnLiveScreenError;
                    _liveScreenService.StreamingStatusChanged += OnLiveScreenStreamingStatusChanged;
                }
                _ = Task.Run(async () =>
                {
                    if (_liveScreenService != null)
                    {
                        await _liveScreenService.ConnectAsync();
                    }
                });
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
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting up hooks: {ex.Message}");
            }
            StartWindowMonitoring();
            StartMouseKeyboardTimer();
            _idleCheckTimer = new Timer(_ => CheckIdleState(), null, 1000, 1000);
            _heartbeatTimer = new Timer(HeartbeatCallback, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        private void HeartbeatCallback(object state)
        {
            if (!_isTracking || _isDisposed) return;
        }

        public async Task StopTrackingAsync(bool sendFinalData = true)
        {
            _screenshotCaptureService?.StopPolling();
            if (_isOnBreak && _currentBreakId.HasValue)
            {
                await EndBreakAsync();
            }
            if (_liveScreenService != null)
            {
                await _liveScreenService.DisconnectAsync();
                _liveScreenService.Dispose();
                _liveScreenService = null;
            }
            if (!_isTracking) return;
            if (sendFinalData && !_isTrackingPaused)
            {
                if (!string.IsNullOrEmpty(_currentAppName))
                    SendApplicationUsageAndReset();
                if (_isBrowserActive)
                    await SaveCurrentBrowserAndPendingUrls();
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
        }
        #endregion

        public void Dispose()
        {
            if (_isDisposed) return;
            _screenshotCaptureService?.Dispose();
            _screenshotCaptureService = null;
            if (_liveScreenService != null)
            {
                _liveScreenService.Dispose();
                _liveScreenService = null;
            }
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            _isDisposed = true;
            StopTrackingAsync(true).Wait(500);
            GC.SuppressFinalize(this);
        }
    }
}