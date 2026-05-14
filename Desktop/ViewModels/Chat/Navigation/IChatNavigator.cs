using Shared.Dto.Call;

namespace Desktop.ViewModels.Chat.Navigation;

public interface IChatNavigator
{
    Task ShowPollDialogAsync(int chatId, Func<Task>? onCreated = null);
    Task ShowEditGroupDialogAsync(ChatDto chat, Action<ChatDto>? onUpdated = null);
    Task NavigateToForwardedChatAsync(ChatDto targetChat);
    Task ShowUserProfileAsync(int userId);
    void OpenCallUi();
    void ShowCallView(CallStateDto state, string chatName, bool isGroupCall);
}