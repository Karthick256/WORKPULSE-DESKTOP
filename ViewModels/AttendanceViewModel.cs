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
using monitor_desktop.Views;

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

        private bool _isOnBreak;
        private BreakType _currentBreakType;
        private string _breakStatus;
        private System.Timers.Timer _breakTimer;
        private string _breakDuration;

        private readonly SemaphoreSlim _breakLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _breakCts;

        public ICommand CheckInCommand { get; private set; }
        public ICommand CheckOutCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }
        public ICommand StartBreakCommand { get; private set; }
        public ICommand EndBreakCommand { get; private set; }

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
                OnPropertyChanged(nameof(CanStartBreak));
                OnPropertyChanged(nameof(CanEndBreak));
                OnPropertyChanged(nameof(BreakButtonText));
                (CheckInCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (CheckOutCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (StartBreakCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (EndBreakCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
                OnPropertyChanged(nameof(CanStartBreak));
                OnPropertyChanged(nameof(CanEndBreak));
                (CheckInCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (CheckOutCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (StartBreakCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (EndBreakCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public bool CanCheckIn => !IsCheckedIn && !IsLoading;
        public bool CanCheckOut => IsCheckedIn && !IsLoading;
        public bool CanStartBreak => IsCheckedIn && !IsOnBreak && !IsLoading;
        public bool CanEndBreak => IsCheckedIn && IsOnBreak && !IsLoading;

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
        public string BreakButtonText => IsOnBreak ? "On Break" : "Start Break";

        public bool IsOnBreak
        {
            get => _isOnBreak;
            set
            {
                if (_isOnBreak == value) return;
                _isOnBreak = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStartBreak));
                OnPropertyChanged(nameof(CanEndBreak));
                OnPropertyChanged(nameof(BreakButtonText));
                (StartBreakCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (EndBreakCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string BreakStatus
        {
            get => _breakStatus;
            set { _breakStatus = value; OnPropertyChanged(); }
        }

        public string BreakDuration
        {
            get => _breakDuration;
            set { _breakDuration = value; OnPropertyChanged(); }
        }

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
            StartBreakCommand = new RelayCommand(_ => _ = StartBreakSafe(), _ => CanStartBreak);
            EndBreakCommand = new RelayCommand(async _ => await EndBreak(), _ => CanEndBreak);
            Application.Current?.Dispatcher.BeginInvoke(new Action(async () =>
            {
                await LoadActiveSession();
            }), DispatcherPriority.Background);
        }

        private async Task StartBreakSafe()
        {
            if (!CanStartBreak) return;

            await _breakLock.WaitAsync();
            try
            {
                _breakCts?.Cancel();
                _breakCts = new CancellationTokenSource();

                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    var breakWindow = new BreakSelectionWindow();
                    breakWindow.Owner = Application.Current.MainWindow;

                    // Use Show() instead of ShowDialog() to prevent blocking
                    var result = breakWindow.ShowDialog();

                    if (result == true && breakWindow.BreakSelected)
                    {
                        await StartBreakInternal(breakWindow.SelectedBreakType, breakWindow.Notes);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelled
            }
            catch (Exception ex)
            {
                await HandleBreakError(ex);
            }
            finally
            {
                _breakLock.Release();
            }
        }

        private async Task StartBreakInternal(BreakType breakType, string notes)
        {
            IsLoading = true;
            StatusMessage = $"Starting {GetBreakTypeName(breakType)} break...";

            try
            {
                // Run the break start in a separate task to avoid UI blocking
                var success = await Task.Run(async () =>
                    await _trackerService.StartBreakAsync(breakType, notes));

                if (success)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        IsOnBreak = true;
                        _currentBreakType = breakType;
                        StatusMessage = $"On {GetBreakTypeName(breakType)} break - Tracking paused";
                        BreakStatus = $"On {GetBreakTypeName(breakType)} break";
                        StartBreakTimer();
                        OnPropertyChanged(nameof(BreakButtonText));
                    });
                }
                else
                {
                    await ShowErrorMessage("Failed to start break. Please try again.");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessage($"Error starting break: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task HandleBreakError(Exception ex)
        {
            Debug.WriteLine($"Break error: {ex.Message}");
            await ShowErrorMessage($"Error starting break: {ex.Message}");
            IsLoading = false;
        }

        private async Task ShowErrorMessage(string message)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                StatusMessage = message;
                MessageBox.Show(message, "Break Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });
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
                        var isOnBreak = await _trackerService.CheckAndRestoreBreakState();
                        IsOnBreak = isOnBreak;

                        if (isOnBreak)
                        {
                            StatusMessage = "On break - Resuming tracking when break ends";
                            StartBreakTimer();
                        }
                        else
                        {
                            StatusMessage = $"Active session since {CurrentSession.CheckInTime.Value:HH:mm:ss} - Tracking active";
                        }
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

            // End break if on break
            if (IsOnBreak)
            {
                await EndBreak();
            }

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
                }

                var checkOutTime = DateTime.Now;
                var response = await _attendanceService.CheckOut(CurrentSession.SessionId.Value, checkOutTime);

                if (response.Status == 200 && response.Data != null)
                {
                    CurrentSession = response.Data;
                    IsCheckedIn = false;
                    StopSessionTimer();
                    StopBreakTimer();

                    var mins = CurrentSession.TotalSessionMinutes ?? 0;
                    var duration = mins >= 60 ? $"{mins / 60}h {mins % 60}m" : $"{mins}m";
                    var productivity = CurrentSession.ProductivityScore ?? 0;
                    StatusMessage = $"Checked out. Duration: {duration} - Tracking stopped";

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(
                            $"Checked out successfully!\n\nDuration: {duration}",
                            "Check-Out Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
                else
                {
                    StatusMessage = response.Message ?? "Check-out failed.";
                    Debug.WriteLine($"Checkout error details: Status={response.Status}, Message={response.Message}");
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

        private async Task StartBreak()
        {
            if (!CanStartBreak) return;

            var breakWindow = new BreakSelectionWindow();
            breakWindow.Owner = Application.Current.MainWindow;

            if (breakWindow.ShowDialog() == true && breakWindow.BreakSelected)
            {
                IsLoading = true;
                StatusMessage = $"Starting {GetBreakTypeName(breakWindow.SelectedBreakType)} break...";

                try
                {
                    var success = await _trackerService.StartBreakAsync(
                        breakWindow.SelectedBreakType,
                        breakWindow.Notes);

                    if (success)
                    {
                        IsOnBreak = true;
                        _currentBreakType = breakWindow.SelectedBreakType;
                        StatusMessage = $"On {GetBreakTypeName(breakWindow.SelectedBreakType)} break - Tracking paused";
                        BreakStatus = $"On {GetBreakTypeName(breakWindow.SelectedBreakType)} break";
                        StartBreakTimer();
                        OnPropertyChanged(nameof(BreakButtonText));
                    }
                    else
                    {
                        StatusMessage = "Failed to start break. Please try again.";
                        MessageBox.Show("Failed to start break. Please try again.",
                            "Break Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error starting break: {ex.Message}";
                    MessageBox.Show($"Error starting break: {ex.Message}",
                        "Break Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    IsLoading = false;
                }
            }
        }

        private async Task EndBreak()
        {
            if (!CanEndBreak) return;

            var confirm = MessageBox.Show(
                $"End your {GetBreakTypeName(_currentBreakType)} break?",
                "End Break",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            IsLoading = true;
            StatusMessage = "Ending break...";

            try
            {
                var success = await _trackerService.EndBreakAsync();

                if (success)
                {
                    IsOnBreak = false;
                    _currentBreakType = BreakType.SHORT_BREAK;
                    StatusMessage = "Break ended - Tracking resumed";
                    BreakStatus = string.Empty;
                    StopBreakTimer();
                    OnPropertyChanged(nameof(BreakButtonText));

                    // Refresh session to get updated break minutes
                    await LoadActiveSession();

                    MessageBox.Show("Break ended successfully. Activity tracking has resumed.",
                        "Break Ended", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusMessage = "Failed to end break. Please try again.";
                    MessageBox.Show("Failed to end break. Please try again or contact support.",
                        "Break Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error ending break: {ex.Message}";
                MessageBox.Show($"Error ending break: {ex.Message}",
                    "Break Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void StartBreakTimer()
        {
            StopBreakTimer();
            _breakTimer = new System.Timers.Timer(1000);
            _breakTimer.AutoReset = true;
            _breakTimer.Elapsed += (_, __) =>
            {
                Application.Current?.Dispatcher.Invoke(() => UpdateBreakDuration());
            };
            _breakTimer.Start();
            UpdateBreakDuration();
        }

        private void StopBreakTimer()
        {
            if (_breakTimer != null)
            {
                _breakTimer.Stop();
                _breakTimer.Dispose();
                _breakTimer = null;
            }
            BreakDuration = string.Empty;
        }

        private void UpdateBreakDuration()
        {
            if (_trackerService.CurrentBreakStartTime.HasValue)
            {
                var duration = DateTime.Now - _trackerService.CurrentBreakStartTime.Value;
                BreakDuration = $"Break duration: {(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
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

        public void Dispose()
        {
            StopSessionTimer();
            StopBreakTimer();
            if (_trackerService != null)
            {
                _trackerService.StatusChanged -= OnTrackerStatusChanged;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}