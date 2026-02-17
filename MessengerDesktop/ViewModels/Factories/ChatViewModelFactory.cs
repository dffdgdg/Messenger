using MessengerDesktop.Services;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Auth;
using MessengerDesktop.Services.Platform;
using MessengerDesktop.Services.Realtime;
using MessengerDesktop.Services.UI;
using MessengerDesktop.ViewModels.Chat;

namespace MessengerDesktop.ViewModels.Factories;

/// <summary>Фабрика для создания <see cref="ChatViewModel"/>.</summary>
public interface IChatViewModelFactory
{
    /// <summary>
    /// Создаёт ViewModel для указанного чата.
    /// </summary>
    /// <param name="chatId">ID чата.</param>
    /// <param name="parent">Родительская ViewModel списка чатов.</param>
    ChatViewModel Create(int chatId, ChatsViewModel parent);
}

/// <inheritdoc />
public class ChatViewModelFactory(
    IApiClientService apiClient,
    IAuthManager authManager,
    IChatInfoPanelStateStore chatInfoPanelStateStore,
    INotificationService notificationService,
    IChatNotificationApiService notificationApiService,
    IGlobalHubConnection globalHub,
    IFileDownloadService fileDownloadService,
    IPlatformService platformService) : IChatViewModelFactory
{
    /// <inheritdoc />
    public ChatViewModel Create(int chatId, ChatsViewModel parent)
    {
        var storageProvider = platformService.MainWindow?.StorageProvider;

        return new ChatViewModel(
            chatId,
            parent,
            apiClient,
            authManager,
            chatInfoPanelStateStore,
            notificationService,
            notificationApiService,
            globalHub,
            fileDownloadService,
            storageProvider);
    }
}