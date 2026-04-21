using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace monitor_desktop.Models.Enums
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AppCategory
    {
        [EnumMember(Value = "DEVELOPMENT")] DEVELOPMENT,
        [EnumMember(Value = "COMMUNICATION")] COMMUNICATION,
        [EnumMember(Value = "BROWSER")] BROWSER,
        [EnumMember(Value = "OFFICE")] OFFICE,
        [EnumMember(Value = "DESIGN")] DESIGN,
        [EnumMember(Value = "ENTERTAINMENT")] ENTERTAINMENT,
        [EnumMember(Value = "SYSTEM")] SYSTEM,
        [EnumMember(Value = "SECURITY")] SECURITY,
        [EnumMember(Value = "DATABASE")] DATABASE,
        [EnumMember(Value = "OTHER")] OTHER
    }
}