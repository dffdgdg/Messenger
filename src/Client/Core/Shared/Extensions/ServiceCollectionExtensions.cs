using Core.Data;
using Core.Data.Repositories.Abstractions;
using Core.Data.Repositories.Implementations;
using Core.Features.Admin.ViewModels;
using Core.Features.Auth.ViewModels;
using Core.Features.Call.ViewModels;
using Core.Features.ChatList.ViewModels.Factories;
using Core.Features.Department.ViewModels;
using Core.Features.Profile.ViewModels;
using Core.Features.Settings.ViewModels;
using Core.Features.Shell;
using Core.Features.Shell.MainMenu;
using Core.Infrastructure.Media;
using Core.Services;
using Core.Services.Api;
using Core.Services.Api.Abstraction;
using Core.Services.Auth;
using Core.Services.Call;
using Core.Services.Call.Abstractions;
using Core.Services.Chat;
using Core.Services.Media.Audio;
using Core.Services.Media.Files;
using Core.Services.Platform.Cache;
using Core.Services.Platform.Navigation;
using Core.Services.Platform.Network;
using Core.Services.Platform.Storage;
using Core.Services.Platform.UI;
using Core.Services.Realtime;
using Core.Services.UI;
using Core.Shared.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;

namespace Core.Shared.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMessengerCoreServices(this IServiceCollection services, string apiBaseUrl)
    {
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
        services.AddSingleton<ISessionStore, SessionStore>();
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
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
#endif
            };
            return new HttpClient(handler)
            {
                BaseAddress = new Uri(apiBaseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
        });

        services.AddSingleton(sp => new AuthenticatedImageLoader(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<ISessionStore>(), apiBaseUrl));

        services.AddSingleton<IAuthService, AuthService>();

        services.AddSingleton<ICookieStorageService>(sp => new CookieStorageService( sp.GetRequiredService<ISecureStorageService>(), sp.GetRequiredService<CookieContainer>(), apiBaseUrl));

        services.AddSingleton<IAuthManager, AuthManager>();

        services.AddSingleton<IApiClientService>(sp => new ApiClientService(sp.GetRequiredService<HttpClient>(),
            sp.GetRequiredService<ISessionStore>(), sp.GetRequiredService<IAuthManager>()));

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IServerDiscoveryService, ServerDiscoveryService>();

        services.AddSingleton<IAudioPlayerService, AudioPlayerService>();


        services.AddSingleton<IGlobalHubConnection, GlobalHubConnection>();

        services.AddSingleton<ICallHubConnection>(sp => new CallHubConnection(
            sp.GetRequiredService<ISessionStore>(), sp.GetRequiredService<ILogger<CallHubConnection>>(), apiBaseUrl));

        services.AddSingleton<ICallService, CallService>();
        services.AddSingleton<ActiveCallStore>();
        services.AddSingleton<CallBannerViewModel>();

        services.AddSingleton<IChatNotificationApiService, ChatNotificationApiService>();
        services.AddSingleton<IChatInfoPanelStateStore, ChatInfoPanelStateStore>();

        services.AddSingleton<IFileDownloadService>(sp => new FileDownloadService(
            sp.GetRequiredService<HttpClient>(),
            sp.GetRequiredService<ISessionStore>(),
            sp.GetRequiredService<IAuthManager>())); services.AddSingleton<IDownloadedFileRepository, DownloadedFileRepository>();
        services.AddSingleton<IFileDownloadStateService, FileDownloadStateService>();
        services.AddSingleton<IDownloadManager, DownloadManager>();

        services.AddSingleton<ChatCoreServices>();
        services.AddSingleton<MediaServices>();
        services.AddSingleton<CallServices>();
        services.AddSingleton<CacheServices>();

        return services;
    }

    public static IServiceCollection AddMessengerViewModels(this IServiceCollection services)
    {
        services.AddSingleton<IChatViewModelFactory, ChatViewModelFactory>();
        services.AddSingleton<IChatListViewModelFactory, ChatListViewModelFactory>();

        services.AddSingleton<MainWindowViewModel>();

        services.AddTransient<UsersTabViewModel>();
        services.AddTransient<DepartmentsTabViewModel>();

        services.AddTransient<LoginViewModel>();
        services.AddTransient<MainMenuViewModel>();
        services.AddTransient<AdminViewModel>();
        services.AddTransient<ProfileViewModel>(sp => new ProfileViewModel(sp.GetRequiredService<IApiClientService>(),
            sp.GetRequiredService<IAuthManager>(),
            sp.GetRequiredService<INotificationService>(),
            sp.GetRequiredService<IGlobalHubConnection>(),
            sp.GetRequiredService<IPlatformService>()));

        services.AddTransient<DepartmentManagementViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services;
    }
}