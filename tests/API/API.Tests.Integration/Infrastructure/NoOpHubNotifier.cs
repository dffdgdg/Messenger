using API.Application.Services.Abstractions;

namespace API.Tests.Integration.Infrastructure;

public class NoOpHubNotifier : IHubNotifier
{
    public Task SendToChatAsync(int chatId, string method, params object?[] args) => Task.CompletedTask;
    public Task SendToUserAsync(int userId, string method, params object?[] args) => Task.CompletedTask;
    public Task AddUserToGroupAsync(int userId, string groupName) => Task.CompletedTask;
    public Task RemoveUserFromGroupAsync(int userId, string groupName) => Task.CompletedTask;
}