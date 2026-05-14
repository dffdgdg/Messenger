namespace API.Services.Abstractions;

public interface ISystemMessageService
{
    Task CreateAsync(int chatId, int senderId, SystemEventType eventType, int? targetUserId = null, string? content = null);
    Task CreateCallEndedMessageAsync(int chatId, int initiatorId, TimeSpan duration);
    Task CreateCallStartedMessageAsync(int chatId, int initiatorId);
}