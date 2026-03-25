using System;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public interface IChatNavigator
{
    Task ShowPollDialogAsync(int chatId, Func<Task>? onCreated = null);
    Task ShowEditGroupDialogAsync(ChatDto chat, Action<ChatDto>? onUpdated = null);
    Task ShowUserProfileAsync(int userId);
}