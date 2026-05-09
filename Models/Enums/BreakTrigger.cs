using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace monitor_desktop.Models.Enums
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BreakTrigger
    {
        [EnumMember(Value = "MANUAL")]
        MANUAL,

        [EnumMember(Value = "AUTOMATIC")]
        AUTOMATIC,

        [EnumMember(Value = "SCHEDULED")]
        SCHEDULED,

        [EnumMember(Value = "FORCED")]
        FORCED
    }
}