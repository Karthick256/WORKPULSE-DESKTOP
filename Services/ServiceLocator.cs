using System;

namespace monitor_desktop.Services
{
    public static class ServiceLocator
    {
        private static ActivityTrackerService _trackerService;
        private static readonly object _lock = new object();

        public static ActivityTrackerService GetTrackerService(ActivityTrackingService trackingService, TokenManager tokenManager)
        {
            lock (_lock)
            {
                if (_trackerService == null || _trackerService.IsDisposed)
                {
                    _trackerService = new ActivityTrackerService(trackingService, tokenManager);
                }
                return _trackerService;
            }
        }

        public static ActivityTrackerService GetExistingTrackerService()
        {
            lock (_lock)
            {
                return _trackerService;
            }
        }

        public static void DisposeTrackerService()
        {
            lock (_lock)
            {
                if (_trackerService != null)
                {
                    _trackerService.StopTracking();
                    _trackerService.Dispose();
                    _trackerService = null;
                }
            }
        }
    }
}