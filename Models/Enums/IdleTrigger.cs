using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace monitor_desktop.Models.Enums
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum IdleTrigger
    {
        [EnumMember(Value = "NO_INPUT")] NO_INPUT,
        [EnumMember(Value = "SCREEN_LOCKED")] SCREEN_LOCKED,
        [EnumMember(Value = "MANUAL_BREAK")] MANUAL_BREAK,
        [EnumMember(Value = "SYSTEM_SLEEP")] SYSTEM_SLEEP,
        [EnumMember(Value = "AWAY_FROM_DESK")] AWAY_FROM_DESK
    }
}