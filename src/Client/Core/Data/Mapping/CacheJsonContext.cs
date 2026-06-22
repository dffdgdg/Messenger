using System.Text.Json.Serialization;

namespace Core.Data.Mappers;

[JsonSerializable(typeof(PollDto))]
[JsonSerializable(typeof(PollOptionDto))]
[JsonSerializable(typeof(PollVoteDto))]
[JsonSerializable(typeof(List<PollOptionDto>))]
[JsonSerializable(typeof(List<PollVoteDto>))]
[JsonSerializable(typeof(List<MessageFileDto>))]
[JsonSerializable(typeof(MessageFileDto))]
public partial class CacheJsonContext : JsonSerializerContext;