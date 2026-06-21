namespace API.Application.Services.Abstractions;

public interface IHubNotifier
{
    Task SendToChatAsync(int chatId, string method, params object?[] args);
    Task SendToUserAsync(int userId, string method, params object?[] args);
    Task AddUserToGroupAsync(int userId, string groupName);
    Task RemoveUserFromGroupAsync(int userId, string groupName);
    Task AddUserToChatGroupAsync(int userId, int chatId);
    Task RemoveUserFromChatGroupAsync(int userId, int chatId);
    Task SendToUserConnectionAsync(string userId, string method, params object?[] args);
}