using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace monitor_desktop.Models.Enums
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BreakType
    {
        [EnumMember(Value = "LUNCH")]
        LUNCH,

        [EnumMember(Value = "SHORT_BREAK")]
        SHORT_BREAK,

        [EnumMember(Value = "LONG_BREAK")]
        LONG_BREAK,

        [EnumMember(Value = "MEETING")]
        MEETING,

        [EnumMember(Value = "TRAINING")]
        TRAINING,

        [EnumMember(Value = "PERSONAL")]
        PERSONAL,

        [EnumMember(Value = "REST")]
        REST,

        [EnumMember(Value = "OTHER")]
        OTHER
    }
}