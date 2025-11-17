using System.Text.Json.Serialization;

namespace MessengerShared.Enum
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum Theme
    {
        light,
        dark,
        system
    }
}
