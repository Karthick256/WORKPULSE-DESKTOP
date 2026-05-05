using System.Text.Json.Serialization;

namespace monitor_desktop.Models.ActivityMonitoring
{
    public class LiveScreenRequestDto
    {
        [JsonPropertyName("sessionId")]
        public long SessionId { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; }

        [JsonPropertyName("quality")]
        public int? Quality { get; set; }

        [JsonPropertyName("fps")]
        public int? Fps { get; set; }

        [JsonPropertyName("duration")]
        public int? Duration { get; set; }
    }

    public class LiveScreenResponseDto
    {
        [JsonPropertyName("streamId")]
        public string StreamId { get; set; }

        [JsonPropertyName("sessionId")]
        public long SessionId { get; set; }

        [JsonPropertyName("username")]
        public string Username { get; set; }

        [JsonPropertyName("workstationName")]
        public string WorkstationName { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("errorMessage")]
        public string ErrorMessage { get; set; }

        [JsonPropertyName("streamUrl")]
        public string StreamUrl { get; set; }

        [JsonPropertyName("startedAt")]
        public DateTime? StartedAt { get; set; }

        [JsonPropertyName("endedAt")]
        public DateTime? EndedAt { get; set; }

        [JsonPropertyName("viewerCount")]
        public int ViewerCount { get; set; }
    }

    public class StreamFrameDto
    {
        [JsonPropertyName("streamId")]
        public string StreamId { get; set; }

        [JsonPropertyName("imageBase64")]
        public string ImageBase64 { get; set; }

        [JsonPropertyName("frameNumber")]
        public long FrameNumber { get; set; }

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; }

        [JsonPropertyName("isKeyFrame")]
        public bool IsKeyFrame { get; set; }
    }
}