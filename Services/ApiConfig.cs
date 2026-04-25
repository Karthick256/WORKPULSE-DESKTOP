namespace monitor_desktop.Services
{
    public static class ApiConfig
    {
        public const string BaseUrl = "https://www.ttassessments.com:2027";
        public const string ApiVersion = "api/v1";
        public const string ApiBaseUrl = BaseUrl + "/" + ApiVersion;

        public const string AuthSignIn = ApiBaseUrl + "/auth/signin";
        public const string AuthSignUp = ApiBaseUrl + "/auth/signup";
        public const string AuthChangePassword = ApiBaseUrl + "/auth/change-password";
        public const string AuthForgotPassword = ApiBaseUrl + "/auth/forgot-password";
        public const string AuthForgotUsername = ApiBaseUrl + "/auth/forgot-username";

        public const string AttendanceCheckIn = ApiBaseUrl + "/attendance/check-in";
        public const string AttendanceCheckOut = ApiBaseUrl + "/attendance/check-out";
        public const string AttendanceActiveSession = ApiBaseUrl + "/attendance/active-session";

        public const string TrackingApplication = ApiBaseUrl + "/tracking/application";
        public const string TrackingBrowser = ApiBaseUrl + "/tracking/browser";
        public const string TrackingBrowserUrl = ApiBaseUrl + "/tracking/browser/url";
        public const string TrackingMouse = ApiBaseUrl + "/tracking/mouse";
        public const string TrackingKeyboard = ApiBaseUrl + "/tracking/keyboard";
        public const string TrackingIdleStart = ApiBaseUrl + "/tracking/idle/start";
        public const string TrackingScreenshotRequest = ApiBaseUrl + "/tracking/admin/screenshot/request";
        public const string TrackingScreenshotPending = ApiBaseUrl + "/tracking/agent/screenshot/pending";
        public const string TrackingScreenshotUpload = ApiBaseUrl + "/tracking/agent/screenshot/upload";
    }
}