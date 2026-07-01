using Core.Infrastructure.Media;
using Core.Services.Api.Abstraction;
using Core.Services.Auth.Abstractions;
using Core.Services.Chat.Abstractions;
using Core.Services.Platform.Abstractions;
using Core.Services.Realtime.Abstractions;

namespace Core.Features.ChatList.ViewModels.Factories;

/// <summary>
/// Базовые сервисы чата: API, авторизация, UI-диалоги и SignalR.
/// </summary>
public sealed class ChatCoreServices(
    IApiClientService apiClient,
    IAuthManager authManager,
    IChatInfoPanelStateStore chatInfoPanelStateStore,
    INotificationService notificationService,
    IChatNotificationApiService notificationApiService,
    IDialogService dialogService,
    IGlobalHubConnection globalHub,
    AuthenticatedImageLoader imageLoader)
{
    public IApiClientService ApiClient { get; } = apiClient;
    public IAuthManager AuthManager { get; } = authManager;
    public IChatInfoPanelStateStore ChatInfoPanelStateStore { get; } = chatInfoPanelStateStore;
    public INotificationService NotificationService { get; } = notificationService;
    public IChatNotificationApiService NotificationApiService { get; } = notificationApiService;
    public IDialogService DialogService { get; } = dialogService;
    public IGlobalHubConnection GlobalHub { get; } = globalHub;
    public AuthenticatedImageLoader ImageLoader { get; } = imageLoader;
}