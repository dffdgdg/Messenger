namespace MessengerShared.Enum;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Theme { light, dark, system }