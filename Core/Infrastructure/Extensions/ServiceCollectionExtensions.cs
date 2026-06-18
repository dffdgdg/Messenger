using Core.Data;
using Core.Data.Repositories.Abstractions;
using Core.Data.Repositories.Implementations;
using Core.Infrastructure.Media;
using Core.Services;
using Core.Services.Abstractions;
using Core.Services.Auth;
using Core.Services.Call;
using Core.Services.Core.Api;
using Core.Services.Core.Auth;
using Core.Services.Core.Realtime;
using Core.Services.Features.Call;
using Core.Services.Features.Chat;
using Core.Services.Features.Media.Files;
using Core.Services.Platform.Cache;
using Core.Services.Platform.Navigation;
using Core.Services.Platform.Network;
using Core.Services.Platform.Storage;
using Core.Services.UI;
using Core.ViewModels;
using Core.ViewModels.Call;
using Core.ViewModels.ChatList.Factories;
using Core.ViewModels.Department;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using Core.Infrastructure.Helpers;


#if ANDROID
using Core.Services.Features.Media.Audio.Platform.Android;
#endif

namespace Core.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMessengerCoreServices(
        this IServiceCollection services, string apiBaseUrl)
    {
        // ── 1. База данных ────────────────────────────────────────────────────
        services.AddSingleton(_ =>
        {
            var dbPath = AppPaths.GetDatabasePath("messenger_cache.db");
            System.Diagnostics.Debug.WriteLine($"[DB] Path: {dbPath}");
            return new LocalDatabase(dbPath);
        });

        services.AddSingleton<IMessageCacheRepository, MessageCacheRepository>();
        services.AddSingleton<IChatCacheRepository, ChatCacheRepository>();
        services.AddSingleton<ILocalCacheService, LocalCacheService>();
        services.AddSingleton<ICacheMaintenanceService, CacheMaintenanceService>();

        // ── 2. Сессия и авторизация ───────────────────────────────────────────
        services.AddSingleton<ISessionStore, SessionStore>();
#if ANDROID
        services.AddSingleton<ISecureStorageService, AndroidSecureStorageService>();
#endif

        // ── 3. HTTP ───────────────────────────────────────────────────────────
        services.AddSingleton<CookieContainer>();

        services.AddSingleton(sp =>
        {
            var cookieContainer = sp.GetRequiredService<CookieContainer>();
            var handler = new HttpClientHandler
            {
                CheckCertificateRevocationList = false,
                UseProxy = false,
                UseCookies = true,
                CookieContainer = cookieContainer,
#if DEBUG
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
#endif
            };
            return new HttpClient(handler)
            {
                BaseAddress = new Uri(apiBaseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
        });

        services.AddSingleton(sp => new AuthenticatedImageLoader(
            sp.GetRequiredService<HttpClient>(),
            sp.GetRequiredService<ISessionStore>(),
            apiBaseUrl));

        // ── 4. Auth ───────────────────────────────────────────────────────────
        services.AddSingleton<IAuthService, AuthService>();

        services.AddSingleton<ICookieStorageService>(sp => new CookieStorageService(
            sp.GetRequiredService<ISecureStorageService>(),
            sp.GetRequiredService<CookieContainer>(),
            apiBaseUrl));

        services.AddSingleton<IAuthManager, AuthManager>();

        services.AddSingleton<IApiClientService>(sp => new ApiClientService(
            sp.GetRequiredService<HttpClient>(),
            sp.GetRequiredService<ISessionStore>(),
            sp.GetRequiredService<IAuthManager>()));

        // ── 5. Платформенные сервисы ──────────────────────────────────────────
#if ANDROID
        services.AddSingleton<IPlatformService, AndroidPlatformService>();
#endif
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IServerDiscoveryService, ServerDiscoveryService>();

        // ── 6. Drawer сервис — платформозависимый ─────────────────────────────
#if ANDROID
        services.AddSingleton<IDrawerService, AndroidDrawerService>();
#endif

        // ── 7. Аудио — платформозависимое ─────────────────────────────────────
#if ANDROID
        services.AddSingleton<ICallAudioService, AndroidCallAudioService>();
#endif

        // ── 8. SignalR / реалтайм ─────────────────────────────────────────────
        services.AddSingleton<IGlobalHubConnection, GlobalHubConnection>();

        services.AddSingleton<ICallHubConnection>(sp => new CallHubConnection(
            sp.GetRequiredService<ISessionStore>(),
            sp.GetRequiredService<ILogger<CallHubConnection>>(),
            apiBaseUrl));

        // ── 9. Звонки — теперь кроссплатформенно ─────────────────────────────
        services.AddSingleton<ICallService, CallService>();
        services.AddSingleton<ActiveCallStore>();
        services.AddSingleton<CallBannerViewModel>();

        // ── 10. Фичи ──────────────────────────────────────────────────────────
        services.AddSingleton<IChatNotificationApiService, ChatNotificationApiService>();
        services.AddSingleton<IChatInfoPanelStateStore, ChatInfoPanelStateStore>();

        // ── 11. Файлы ─────────────────────────────────────────────────────────
        services.AddSingleton<IFileDownloadService>(sp =>
            new FileDownloadService(sp.GetRequiredService<HttpClient>()));
        services.AddSingleton<IDownloadedFileRepository, DownloadedFileRepository>();
        services.AddSingleton<IFileDownloadStateService, FileDownloadStateService>();

        // ── 12. Фабрики VM ────────────────────────────────────────────────────
        services.AddSingleton<ChatCoreServices>();
        services.AddSingleton<MediaServices>();
        services.AddSingleton<CallServices>();
        services.AddSingleton<CacheServices>();
        services.AddSingleton<ChatViewModelDependencies>();

        return services;
    }

    public static IServiceCollection AddMessengerViewModels(this IServiceCollection services)
    {
        services.AddSingleton<IChatViewModelFactory, ChatViewModelFactory>();
        services.AddSingleton<IChatsViewModelFactory, ChatsViewModelFactory>();

        services.AddSingleton<MainWindowViewModel>();

        services.AddTransient<UsersTabViewModel>();
        services.AddTransient<DepartmentsTabViewModel>();

        services.AddTransient<LoginViewModel>();
        services.AddTransient<MainMenuViewModel>();
        services.AddTransient<AdminViewModel>();
        services.AddTransient<ProfileViewModel>(sp => new ProfileViewModel(
            sp.GetRequiredService<IApiClientService>(),
            sp.GetRequiredService<IAuthManager>(),
            sp.GetRequiredService<INotificationService>(),
            sp.GetRequiredService<IGlobalHubConnection>(),
            sp.GetRequiredService<IPlatformService>()));

        services.AddTransient<DepartmentManagementViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services;
    }
}