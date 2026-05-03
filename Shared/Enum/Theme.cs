namespace Shared.Enum;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Theme { light, dark, system }