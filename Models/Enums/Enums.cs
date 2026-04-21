using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace monitor_desktop.Models.Enums
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SessionStatus
    {
        [EnumMember(Value = "ACTIVE")] ACTIVE,
        [EnumMember(Value = "COMPLETED")] COMPLETED,
        [EnumMember(Value = "ABANDONED")] ABANDONED
    }
}