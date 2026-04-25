using System;
using System.Text.Json.Serialization;

namespace monitor_desktop.Models.ActivityMonitoring
{
    public class ScreenshotResponseDto
    {
        [JsonPropertyName("requestId")]
        public long RequestId { get; set; }

        [JsonPropertyName("sessionId")]
        public long SessionId { get; set; }

        [JsonPropertyName("username")]
        public string Username { get; set; }

        [JsonPropertyName("workstationName")]
        public string WorkstationName { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("requestReason")]
        public string RequestReason { get; set; }

        [JsonPropertyName("requestedAt")]
        public DateTime RequestedAt { get; set; }

        [JsonPropertyName("respondedAt")]
        public DateTime? RespondedAt { get; set; }

        [JsonPropertyName("errorMessage")]
        public string ErrorMessage { get; set; }

        [JsonPropertyName("screenshotId")]
        public long? ScreenshotId { get; set; }

        [JsonPropertyName("imageSizeKB")]
        public int? ImageSizeKB { get; set; }
    }

    public class ScreenshotRequestDto
    {
        [JsonPropertyName("sessionId")]
        public long SessionId { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; }
    }

    public class DesktopAgentScreenshotUploadDto
    {
        [JsonPropertyName("requestId")]
        public long RequestId { get; set; }

        [JsonPropertyName("imageBase64")]
        public string ImageBase64 { get; set; }

        [JsonPropertyName("imageFormat")]
        public string ImageFormat { get; set; }

        [JsonPropertyName("sessionId")]
        public long SessionId { get; set; }

        [JsonPropertyName("errorMessage")]
        public string ErrorMessage { get; set; }

        [JsonPropertyName("success")]
        public bool Success { get; set; }
    }

    public class ScreenshotImageDto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("sessionId")]
        public long SessionId { get; set; }

        [JsonPropertyName("username")]
        public string Username { get; set; }

        [JsonPropertyName("imageBase64")]
        public string ImageBase64 { get; set; }

        [JsonPropertyName("imageFormat")]
        public string ImageFormat { get; set; }

        [JsonPropertyName("capturedAt")]
        public DateTime CapturedAt { get; set; }

        [JsonPropertyName("imageSizeKB")]
        public int ImageSizeKB { get; set; }
    }

    public class ApiResponseList<T>
    {
        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("data")]
        public List<T> Data { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }
    }
}