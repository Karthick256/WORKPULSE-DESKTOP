
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using monitor_desktop.Models.ActivityMonitoring;
using monitor_desktop.Models.Enums;
using monitor_desktop.Services;
using monitor_desktop.Helpers;
using System.Windows.Input;

namespace monitor_desktop.ViewModels
{
    public class AttendanceViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly AttendanceService _attendanceService;
        private readonly ApiClient _apiClient;
        private readonly ActivityTrackingService _activityTrackingService;
        private readonly TokenManager _tokenManager;
        private ActivityTrackerService _trackerService;

        private AttendanceSessionResponse _currentSession;
        private bool _isCheckedIn;
        private bool _isLoading;
        private string _statusMessage;
        private System.Timers.Timer _sessionTimer;
        private string _sessionDuration;

        public ICommand CheckInCommand { get; private set; }
        public ICommand CheckOutCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }

        public AttendanceSessionResponse CurrentSession
        {
            get => _currentSession;
            set
            {
                _currentSession = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanCheckIn));
                OnPropertyChanged(nameof(CanCheckOut));
                (CheckInCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (CheckOutCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
                (CheckInCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (CheckOutCommand as RelayCommand)?.RaiseCanExecuteChanged();
                CommandManager.InvalidateRequerySuggested();

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
                _isLoading = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanCheckIn));
                OnPropertyChanged(nameof(CanCheckOut));
                (CheckInCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (CheckOutCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public bool CanCheckIn => !IsCheckedIn && !IsLoading;
        public bool CanCheckOut => IsCheckedIn && !IsLoading;

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public string SessionDuration
        {
            get => _sessionDuration;
            set { _sessionDuration = value; OnPropertyChanged(); }
        }

        public string CheckInButtonText => IsCheckedIn ? "Checked In ✓" : "Check In";
        public string CheckOutButtonText => IsCheckedIn ? "Check Out" : "No Active Session";

        public AttendanceViewModel()
        {
            var tokenManager = new TokenManager();
            _apiClient = new ApiClient(tokenManager);
            _attendanceService = new AttendanceService(_apiClient);
            _activityTrackingService = new ActivityTrackingService(_apiClient);
            _tokenManager = tokenManager;

            _trackerService = ActivityTrackerService.GetInstance(_activityTrackingService, _tokenManager);
            _trackerService.StatusChanged += OnTrackerStatusChanged;

            CheckInCommand = new RelayCommand(async _ => await CheckIn(), _ => CanCheckIn);
            CheckOutCommand = new RelayCommand(async _ => await CheckOut(), _ => CanCheckOut);
            RefreshCommand = new RelayCommand(async _ => await LoadActiveSession());

            Application.Current?.Dispatcher.BeginInvoke(new Action(async () =>
            {
                await LoadActiveSession();
            }), DispatcherPriority.Background);
        }

        private void OnTrackerStatusChanged(object sender, string status)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                StatusMessage = status;
            });
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
                Debug.WriteLine($"LoadActiveSession Error: {ex.Message}");
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
                            await _trackerService.StopTrackingAsync(true);
                        }
                        _trackerService.StartTracking(CurrentSession.SessionId.Value);
                        StatusMessage = $"Checked in at {CurrentSession.CheckInTime.Value:HH:mm:ss} - Tracking active";
                    }
                    else
                    {
                        StatusMessage = $"Checked in at {CurrentSession.CheckInTime.Value:HH:mm:ss}";
                    }

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(
                            $"Checked in successfully!\n\nWorkstation: {CurrentSession.WorkstationName}\nTime: {CurrentSession.CheckInTime.Value:HH:mm:ss}\n\nActivity tracking is active.",
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
                Debug.WriteLine($"CheckIn Error: {ex.Message}");
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

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(
                            $"Checked out successfully!\n\nDuration: {duration}\nProductivity: {productivity}%",
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
                Debug.WriteLine($"CheckOut Error: {ex.Message}");
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

        public void Dispose()
        {
            if (_trackerService != null)
            {
                _trackerService.StatusChanged -= OnTrackerStatusChanged;
            }
            StopSessionTimer();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}