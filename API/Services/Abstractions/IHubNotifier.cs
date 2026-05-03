namespace API.Services.Abstractions;

public interface IHubNotifier
{
    Task SendToChatAsync(int chatId, string method, params object?[] args);
    Task SendToUserAsync(int userId, string method, params object?[] args);
}