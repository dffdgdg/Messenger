namespace API.Application.Services.Abstractions;

public interface IHubNotifier
{
    Task SendToChatAsync(int chatId, string method, params object?[] args);
    Task SendToUserAsync(int userId, string method, params object?[] args);

    /// <summary>
    /// Добавляет все активные соединения пользователя в произвольную SignalR-группу.
    /// </summary>
    Task AddUserToGroupAsync(int userId, string groupName);

    /// <summary>
    /// Удаляет все активные соединения пользователя из произвольной SignalR-группы.
    /// </summary>
    Task RemoveUserFromGroupAsync(int userId, string groupName);

    /// <summary>
    /// Добавляет пользователя в группу чата chat_{chatId}.
    /// </summary>
    Task AddUserToChatGroupAsync(int userId, int chatId)
        => AddUserToGroupAsync(userId, $"chat_{chatId}");

    /// <summary>
    /// Удаляет пользователя из группы чата chat_{chatId}.
    /// </summary>
    Task RemoveUserFromChatGroupAsync(int userId, int chatId)
        => RemoveUserFromGroupAsync(userId, $"chat_{chatId}");

    /// <summary>
    /// Отправляет событие конкретному пользователю через его группу user_{userId}.
    /// Используется когда нужно уведомить пользователя об изменении его прав/роли.
    /// </summary>
    Task SendToUserConnectionAsync(string userId, string method, params object?[] args)
        => int.TryParse(userId, out var id) ? SendToUserAsync(id, method, args) : Task.CompletedTask;
}
