using monitor_desktop.Models.ActivityMonitoring;
using monitor_desktop.Models.Enums;
using monitor_desktop.Services;
using monitor_desktop.Helpers;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Diagnostics;

namespace monitor_desktop.ViewModels
{
    public class AttendanceViewModel : INotifyPropertyChanged, IDisposable
    {
        // ── Services ─────────────────────────────────────────────────────────
        private readonly AttendanceService _attendanceService;
        private readonly ApiClient _apiClient;
        private readonly ActivityTrackingService _activityTrackingService;
        private readonly TokenManager _tokenManager;
        private ActivityTrackerService _trackerService;

        // ── Backing fields ────────────────────────────────────────────────────
        private AttendanceSessionResponse _currentSession;
        private bool _isCheckedIn;
        private bool _isLoading;
        private string _statusMessage;
        private DateTime _selectedDate;
        private DateTime _dateRangeFrom;
        private DateTime _dateRangeTo;
        private ObservableCollection<AttendanceSessionResponse> _sessions;
        private AttendanceSessionResponse _selectedSession;
        private bool _isAdmin;
        private bool _isTodaySelected = true;
        private bool _isByDateSelected;
        private bool _isDateRangeSelected;
        private bool _isAdminViewSelected;
        private System.Timers.Timer _sessionTimer;
        private string _sessionDuration;

        // ── Commands ─────────────────────────────────────────────────────────
        public ICommand CheckInCommand { get; private set; }
        public ICommand CheckOutCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }
        public ICommand LoadTodaySessionsCommand { get; private set; }
        public ICommand LoadByDateCommand { get; private set; }
        public ICommand LoadByRangeCommand { get; private set; }
        public ICommand LoadAdminViewCommand { get; private set; }

        // ── Constructor ───────────────────────────────────────────────────────
        public AttendanceViewModel()
        {
            _apiClient = new ApiClient();
            _attendanceService = new AttendanceService(_apiClient);
            _activityTrackingService = new ActivityTrackingService(_apiClient);
            _tokenManager = new TokenManager();

            _trackerService = ServiceLocator.GetTrackerService(_activityTrackingService, _tokenManager);
            _trackerService.StatusChanged += OnTrackerStatusChanged;

            SelectedDate = DateTime.Today;
            DateRangeFrom = DateTime.Today.AddDays(-7);
            DateRangeTo = DateTime.Today;
            Sessions = new ObservableCollection<AttendanceSessionResponse>();
            StatusMessage = "Ready";

            IsAdmin = _tokenManager.CurrentToken?.IsAdmin ?? false;

            CheckInCommand = new RelayCommand(async _ => await CheckIn(), _ => CanCheckIn);
            CheckOutCommand = new RelayCommand(async _ => await CheckOut(), _ => CanCheckOut);
            RefreshCommand = new RelayCommand(async _ => await RefreshData());
            LoadTodaySessionsCommand = new RelayCommand(async _ => await LoadTodaySessions());
            LoadByDateCommand = new RelayCommand(async _ => await LoadSessionsByDate(SelectedDate));
            LoadByRangeCommand = new RelayCommand(async _ => await LoadSessionsByDateRange());
            LoadAdminViewCommand = new RelayCommand(async _ => await LoadAllSessionsByDateRange());

            Application.Current?.Dispatcher.BeginInvoke(new Action(async () =>
            {
                await InitialLoadAsync();
            }), DispatcherPriority.Background);
        }

        private void OnTrackerStatusChanged(object sender, string status)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                StatusMessage = status;
            });
        }

        // ── Properties ────────────────────────────────────────────────────────
        public AttendanceSessionResponse CurrentSession
        {
            get => _currentSession;
            set
            {
                _currentSession = value;
                OnPropertyChanged();
                RefreshCommands();
            }
        }

        public bool IsCheckedIn
        {
            get => _isCheckedIn;
            set
            {
                if (_isCheckedIn == value) return;
                _isCheckedIn = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CheckInButtonText));
                OnPropertyChanged(nameof(CheckOutButtonText));
                OnPropertyChanged(nameof(CanCheckIn));
                OnPropertyChanged(nameof(CanCheckOut));

                RefreshCommands();

                if (_isCheckedIn)
                    StartSessionTimer();
                else
                    StopSessionTimer();
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading == value) return;
                _isLoading = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanCheckIn));
                OnPropertyChanged(nameof(CanCheckOut));
                RefreshCommands();
            }
        }

        public bool CanCheckIn => !IsCheckedIn && !IsLoading;
        public bool CanCheckOut => IsCheckedIn && !IsLoading;

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public string SessionDuration
        {
            get => _sessionDuration;
            set
            {
                _sessionDuration = value;
                OnPropertyChanged();
            }
        }

        public string CheckInButtonText => IsCheckedIn ? "Checked In ✓" : "Check In";
        public string CheckOutButtonText => IsCheckedIn ? "Check Out" : "No Active Session";

        public DateTime SelectedDate
        {
            get => _selectedDate;
            set { _selectedDate = value; OnPropertyChanged(); }
        }

        public DateTime DateRangeFrom
        {
            get => _dateRangeFrom;
            set { _dateRangeFrom = value; OnPropertyChanged(); }
        }

        public DateTime DateRangeTo
        {
            get => _dateRangeTo;
            set { _dateRangeTo = value; OnPropertyChanged(); }
        }

        public ObservableCollection<AttendanceSessionResponse> Sessions
        {
            get => _sessions;
            set { _sessions = value; OnPropertyChanged(); }
        }

        public AttendanceSessionResponse SelectedSession
        {
            get => _selectedSession;
            set { _selectedSession = value; OnPropertyChanged(); }
        }

        public bool IsAdmin
        {
            get => _isAdmin;
            set { _isAdmin = value; OnPropertyChanged(); }
        }

        public bool IsTodaySelected
        {
            get => _isTodaySelected;
            set
            {
                _isTodaySelected = value;
                OnPropertyChanged();
                if (value)
                    _ = LoadTodaySessions();
            }
        }

        public bool IsByDateSelected
        {
            get => _isByDateSelected;
            set
            {
                _isByDateSelected = value;
                OnPropertyChanged();
                if (value)
                    _ = LoadSessionsByDate(SelectedDate);
            }
        }

        public bool IsDateRangeSelected
        {
            get => _isDateRangeSelected;
            set
            {
                _isDateRangeSelected = value;
                OnPropertyChanged();
                if (value)
                    _ = LoadSessionsByDateRange();
            }
        }

        public bool IsAdminViewSelected
        {
            get => _isAdminViewSelected;
            set
            {
                _isAdminViewSelected = value;
                OnPropertyChanged();
                if (value && IsAdmin)
                    _ = LoadAllSessionsByDateRange();
            }
        }

        // ── Helper Methods ───────────────────────────────────────────────────

        private void RefreshCommands()
        {
            (CheckInCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CheckOutCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        // ── Core operations ───────────────────────────────────────────────────

        private async Task InitialLoadAsync()
        {
            await LoadActiveSession();
            await LoadTodaySessions();
        }

        public async Task LoadActiveSession()
        {
            IsLoading = true;
            try
            {
                var response = await _attendanceService.GetActiveSession();
                if (response.Status == 200 && response.Data != null)
                {
                    CurrentSession = response.Data;
                    IsCheckedIn = CurrentSession.SessionStatus == SessionStatus.ACTIVE;

                    if (IsCheckedIn && CurrentSession.SessionId.HasValue)
                    {
                        if (!_trackerService.IsTracking || _trackerService.CurrentSessionId != CurrentSession.SessionId.Value)
                        {
                            _trackerService.StartTracking(CurrentSession.SessionId.Value);
                        }
                        StatusMessage = $"Active session since {CurrentSession.CheckInTime.Value:HH:mm:ss} - Tracking active";
                    }
                    else if (IsCheckedIn && CurrentSession.CheckInTime.HasValue)
                    {
                        StatusMessage = $"Active session since {CurrentSession.CheckInTime.Value:HH:mm:ss}";
                    }
                    else
                    {
                        StatusMessage = "No active session. Click Check In to start.";
                        IsCheckedIn = false;
                        CurrentSession = null;
                    }
                }
                else
                {
                    IsCheckedIn = false;
                    CurrentSession = null;
                    StatusMessage = "No active session. Click Check In to start.";
                }
            }
            catch (Exception ex)
            {
                IsCheckedIn = false;
                CurrentSession = null;
                StatusMessage = $"Error loading session: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"LoadActiveSession Error: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task CheckIn()
        {
            if (IsCheckedIn || IsLoading) return;

            IsLoading = true;
            StatusMessage = "Checking in...";

            try
            {
                var response = await _attendanceService.AutoCheckIn();

                if ((response.Status == 200 || response.Status == 201) && response.Data != null)
                {
                    CurrentSession = response.Data;
                    IsCheckedIn = true;

                    if (CurrentSession.SessionId.HasValue)
                    {
                        if (_trackerService.IsTracking && _trackerService.CurrentSessionId != CurrentSession.SessionId.Value)
                        {
                            _trackerService.StopTracking();
                        }
                        _trackerService.StartTracking(CurrentSession.SessionId.Value);
                        StatusMessage = $"Checked in at {CurrentSession.CheckInTime.Value:HH:mm:ss} - Tracking active";
                    }
                    else
                    {
                        StatusMessage = $"Checked in at {CurrentSession.CheckInTime.Value:HH:mm:ss}";
                    }

                    await LoadTodaySessions();

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(
                            $"Checked in successfully!\n\nWorkstation: {CurrentSession.WorkstationName}\nTime: {CurrentSession.CheckInTime.Value:HH:mm:ss}\n\nActivity tracking is active and will continue until you check out.",
                            "Check-In Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
                else
                {
                    StatusMessage = response.Message ?? "Check-in failed. Please try again.";
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(StatusMessage, "Check-In Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Check-in error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"CheckIn Error: {ex.Message}");
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(StatusMessage, "Check-In Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task CheckOut()
        {
            if (!IsCheckedIn || IsLoading || CurrentSession?.SessionId == null) return;

            var confirm = MessageBox.Show(
                "Are you sure you want to check out?\n\nActivity tracking will stop.",
                "Confirm Check-Out", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            IsLoading = true;
            StatusMessage = "Checking out...";

            try
            {
                if (_trackerService.IsTracking)
                {
                    await _trackerService.StopTrackingAsync(true);
                    Debug.WriteLine("Tracking stopped before checkout");
                }

                var response = await _attendanceService.CheckOut(CurrentSession.SessionId.Value);

                if (response.Status == 200 && response.Data != null)
                {
                    CurrentSession = response.Data;
                    IsCheckedIn = false;
                    StopSessionTimer();

                    var mins = CurrentSession.TotalSessionMinutes ?? 0;
                    var duration = mins >= 60 ? $"{mins / 60}h {mins % 60}m" : $"{mins}m";
                    var productivity = CurrentSession.ProductivityScore ?? 0;
                    StatusMessage = $"Checked out. Duration: {duration}, Productivity: {productivity}% - Tracking stopped";

                    await LoadTodaySessions();

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(
                            $"Checked out successfully!\n\nDuration: {duration}\nProductivity: {productivity}%\n\nActivity tracking has been stopped.",
                            "Check-Out Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
                else
                {
                    StatusMessage = response.Message ?? "Check-out failed.";
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(StatusMessage, "Check-Out Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Check-out error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"CheckOut Error: {ex.Message}");
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(StatusMessage, "Check-Out Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                IsLoading = false;
            }
        }

        // ── Session history ───────────────────────────────────────────────────

        public async Task LoadTodaySessions() => await LoadSessionsByDate(DateTime.Today);

        public async Task LoadSessionsByDate(DateTime date)
        {
            IsLoading = true;
            try
            {
                var response = await _attendanceService.GetSessionsByDate(date);

                await Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    Sessions.Clear();
                    if (response.Status == 200 && response.Data?.Count > 0)
                    {
                        foreach (var s in response.Data.OrderByDescending(x => x.CheckInTime))
                            Sessions.Add(s);
                        StatusMessage = $"{Sessions.Count} session(s) on {date:MMM dd, yyyy}";
                    }
                    else
                    {
                        StatusMessage = $"No sessions on {date:MMM dd, yyyy}";
                    }
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"LoadSessionsByDate Error: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task LoadSessionsByDateRange()
        {
            IsLoading = true;
            try
            {
                var response = await _attendanceService.GetSessionsByDateRange(DateRangeFrom, DateRangeTo);

                await Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    Sessions.Clear();
                    if (response.Status == 200 && response.Data?.Count > 0)
                    {
                        foreach (var s in response.Data.OrderByDescending(x => x.WorkDate))
                            Sessions.Add(s);
                        StatusMessage = $"{Sessions.Count} session(s) · {DateRangeFrom:MMM dd}–{DateRangeTo:MMM dd, yyyy}";
                    }
                    else
                    {
                        StatusMessage = "No sessions in this range";
                    }
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"LoadSessionsByDateRange Error: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task LoadAllSessionsByDateRange()
        {
            if (!IsAdmin)
            {
                StatusMessage = "Admin access required";
                return;
            }

            IsLoading = true;
            try
            {
                var response = await _attendanceService.GetAllSessionsByDateRange(DateRangeFrom, DateRangeTo);

                await Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    Sessions.Clear();
                    if (response.Status == 200 && response.Data?.Count > 0)
                    {
                        foreach (var s in response.Data.OrderByDescending(x => x.WorkDate).ThenBy(x => x.Username))
                            Sessions.Add(s);
                        StatusMessage = $"{Sessions.Count} sessions across all employees";
                    }
                    else
                    {
                        StatusMessage = "No sessions in this range";
                    }
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"LoadAllSessionsByDateRange Error: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task RefreshData()
        {
            StatusMessage = "Refreshing...";
            await LoadActiveSession();
            await LoadTodaySessions();
        }

        // ── Timer ─────────────────────────────────────────────────────────────
        private void StartSessionTimer()
        {
            StopSessionTimer();
            _sessionTimer = new System.Timers.Timer(1000);
            _sessionTimer.AutoReset = true;
            _sessionTimer.Elapsed += (_, __) =>
            {
                Application.Current?.Dispatcher.Invoke(() => UpdateSessionDuration());
            };
            _sessionTimer.Start();
            UpdateSessionDuration();
        }

        private void StopSessionTimer()
        {
            if (_sessionTimer != null)
            {
                _sessionTimer.Stop();
                _sessionTimer.Dispose();
                _sessionTimer = null;
            }
            SessionDuration = string.Empty;
        }

        private void UpdateSessionDuration()
        {
            if (CurrentSession?.CheckInTime == null || !IsCheckedIn) return;
            var duration = DateTime.Now - CurrentSession.CheckInTime.Value;
            SessionDuration = $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
        }

        // ── Cleanup ──────────────────────────────────────────────────────────
        public void Dispose()
        {
            if (_trackerService != null)
            {
                _trackerService.StatusChanged -= OnTrackerStatusChanged;
            }
            StopSessionTimer();
        }

        // ── INotifyPropertyChanged ────────────────────────────────────────────

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}