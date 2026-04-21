using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace monitor_desktop.Models.Enums
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UrlCategory
    {
        [EnumMember(Value = "WORK")] WORK,
        [EnumMember(Value = "SOCIAL_MEDIA")] SOCIAL_MEDIA,
        [EnumMember(Value = "SOCIAL")] SOCIAL,
        [EnumMember(Value = "COMMUNICATION")] COMMUNICATION,
        [EnumMember(Value = "NEWS")] NEWS,
        [EnumMember(Value = "ENTERTAINMENT")] ENTERTAINMENT,
        [EnumMember(Value = "SHOPPING")] SHOPPING,
        [EnumMember(Value = "EMAIL")] EMAIL,
        [EnumMember(Value = "DEVELOPMENT")] DEVELOPMENT,
        [EnumMember(Value = "LEARNING")] LEARNING,
        [EnumMember(Value = "SEARCH")] SEARCH,
        [EnumMember(Value = "OTHER")] OTHER
    }
}