using System.Text.Json;
using System.Text.Json.Serialization;

namespace monitor_desktop.Converters
{
    public class CustomDateTimeConverter : JsonConverter<DateTime>
    {
        private const string WriteFormat = "yyyy-MM-ddTHH:mm:ss.fff";

        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var str = reader.GetString();

            if (string.IsNullOrEmpty(str))
                return default;

            if (DateTime.TryParse(str, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                return dt;

            if (DateTime.TryParseExact(str, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dateOnly))
                return dateOnly;

            return default;
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString(WriteFormat,
                System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    public class CustomNullableDateTimeConverter : JsonConverter<DateTime?>
    {
        private readonly CustomDateTimeConverter _inner = new();

        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            return _inner.Read(ref reader, typeof(DateTime), options);
        }

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (value == null)
                writer.WriteNullValue();
            else
                _inner.Write(writer, value.Value, options);
        }
    }
}