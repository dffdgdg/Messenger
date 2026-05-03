using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Desktop.Data.Mappers;

[JsonSerializable(typeof(PollDto))]
[JsonSerializable(typeof(List<MessageFileDto>))]
public partial class CacheJsonContext : JsonSerializerContext;