using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MessengerDesktop.Data.Mappers;

[JsonSerializable(typeof(PollDto))]
[JsonSerializable(typeof(List<MessageFileDto>))]
public partial class CacheJsonContext : JsonSerializerContext;