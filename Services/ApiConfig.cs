namespace monitor_desktop.Services
{
    public static class ApiConfig
    {
        public const string BaseUrl = "https://www.ttassessments.com:2027";
        public const string ApiVersion = "api/v1";
        public const string ApiBaseUrl = BaseUrl + "/" + ApiVersion;

        // Authentication endpoints
        public const string AuthSignIn = ApiBaseUrl + "/auth/signin";
        public const string AuthSignUp = ApiBaseUrl + "/auth/signup";
        public const string AuthChangePassword = ApiBaseUrl + "/auth/change-password";
        public const string AuthForgotPassword = ApiBaseUrl + "/auth/forgot-password";
        public const string AuthForgotUsername = ApiBaseUrl + "/auth/forgot-username";

        // Attendance endpoints
        public const string AttendanceCheckIn = ApiBaseUrl + "/attendance/check-in";
        public const string AttendanceCheckOut = ApiBaseUrl + "/attendance/check-out";
        public const string AttendanceActiveSession = ApiBaseUrl + "/attendance/active-session";
        public const string AttendanceSessionsByDate = ApiBaseUrl + "/attendance/sessions/by-date";
        public const string AttendanceSessionsByRange = ApiBaseUrl + "/attendance/sessions/by-range";
        public const string AttendanceAdminSessionsByRange = ApiBaseUrl + "/attendance/admin/sessions/by-range";

        // Activity Tracking endpoints
        public const string TrackingBatch = ApiBaseUrl + "/tracking/batch";
        public const string TrackingApplication = ApiBaseUrl + "/tracking/application";
        public const string TrackingBrowser = ApiBaseUrl + "/tracking/browser";
        public const string TrackingBrowserUrl = ApiBaseUrl + "/tracking/browser/url";
        public const string TrackingMouse = ApiBaseUrl + "/tracking/mouse";
        public const string TrackingKeyboard = ApiBaseUrl + "/tracking/keyboard";
        public const string TrackingIdleStart = ApiBaseUrl + "/tracking/idle/start";
        public const string TrackingIdleEnd = ApiBaseUrl + "/tracking/idle/end";

        // Profile endpoints
        public const string ProfilesGetMy = ApiBaseUrl + "/profiles/get-my-profile";
        public const string ProfilesUpdateMy = ApiBaseUrl + "/profiles/update-my-profile";
        public const string ProfilesGetImage = ApiBaseUrl + "/profiles/get-profile-image";
        public const string ProfilesUploadImage = ApiBaseUrl + "/profiles/upload-profile-image";
        public const string ProfilesUpdateImage = ApiBaseUrl + "/profiles/update-profile-image";
        public const string ProfilesDeleteImage = ApiBaseUrl + "/profiles/delete-profile-image";
    }
}