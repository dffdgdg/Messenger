using MessengerDesktop.Data;
using MessengerDesktop.Data.Repositories.Abstractions;
using MessengerDesktop.Data.Repositories.Implementations;
using MessengerDesktop.Services;
using MessengerDesktop.Services.Auth;
using MessengerDesktop.Services.Call;
using MessengerDesktop.Services.Core.Api;
using MessengerDesktop.Services.Core.Auth;
using MessengerDesktop.Services.Core.Realtime;
using MessengerDesktop.Services.Features.Call;
using MessengerDesktop.Services.Features.Chat;
using MessengerDesktop.Services.Features.Media.Audio;
using MessengerDesktop.Services.Features.Media.Files;
using MessengerDesktop.Services.Platform.Cache;
using MessengerDesktop.Services.Platform.Navigation;
using MessengerDesktop.Services.Platform.Network;
using MessengerDesktop.Services.Platform.OS;
using MessengerDesktop.Services.Platform.Storage;
using MessengerDesktop.Services.UI;
using MessengerDesktop.ViewModels;
using MessengerDesktop.ViewModels.Call;
using MessengerDesktop.ViewModels.ChatList.Factories;
using MessengerDesktop.ViewModels.Department;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;

namespace MessengerDesktop.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMessengerCoreServices(this IServiceCollection services, string apiBaseUrl)
    {
        services.AddSingleton(_ =>
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dbDir = Path.Combine(appData, "MessengerDesktop");
            Directory.CreateDirectory(dbDir);
            var dbPath = Path.Combine(dbDir, "messenger_cache.db");
            return new LocalDatabase(dbPath);
        });

        services.AddSingleton<IMessageCacheRepository, MessageCacheRepository>();
        services.AddSingleton<IChatCacheRepository, ChatCacheRepository>();
        services.AddSingleton<ILocalCacheService, LocalCacheService>();
        services.AddSingleton<ICacheMaintenanceService, CacheMaintenanceService>();
        services.AddSingleton<IPlatformService, PlatformService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IGlobalHubConnection, GlobalHubConnection>();
        services.AddSingleton<IChatNotificationApiService, ChatNotificationApiService>();
        services.AddSingleton<IChatInfoPanelStateStore, ChatInfoPanelStateStore>();
        services.AddSingleton<IAudioPlayerService, AudioPlayerService>();
        services.AddSingleton<PortAudioLifetime>();
        services.AddSingleton<CallAudioService>();

        services.AddSingleton<ICallHubConnection>(sp => new CallHubConnection(sp.GetRequiredService<ISessionStore>(), sp.GetRequiredService<ILogger<CallHubConnection>>(), apiBaseUrl));

        services.AddSingleton<ICallService, CallService>();
        services.AddSingleton<ActiveCallStore>();
        services.AddSingleton<CallBannerViewModel>();

        services.AddSingleton(_ =>
        {
            var handler = new HttpClientHandler
            {
                CheckCertificateRevocationList = false,
                UseProxy = false,
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

        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<ISessionStore, SessionStore>();
        services.AddSingleton<ISecureStorageService, SecureStorageService>();
        services.AddSingleton<IAuthManager, AuthManager>();

        services.AddSingleton<IApiClientService>(sp => new ApiClientService(
        sp.GetRequiredService<HttpClient>(),
        sp.GetRequiredService<ISessionStore>(),
        sp.GetRequiredService<IAuthManager>()));

        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<INotificationService, NotificationService>();

        services.AddSingleton<IFileDownloadService>(sp =>
        {
            var httpClient = sp.GetRequiredService<HttpClient>();
            return new FileDownloadService(httpClient);
        });
        services.AddSingleton<IAudioRecorderService, AudioRecorderService>();
        services.AddSingleton<ChatCoreServices>();
        services.AddSingleton<MediaServices>();
        services.AddSingleton<CallServices>();
        services.AddSingleton<CacheServices>();
        services.AddSingleton<ChatViewModelDependencies>();
        services.AddSingleton<IServerDiscoveryService, ServerDiscoveryService>();

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
        services.AddTransient<ProfileViewModel>(sp => new ProfileViewModel(sp.GetRequiredService<IApiClientService>(),
            sp.GetRequiredService<IAuthManager>(), sp.GetRequiredService<INotificationService>(),
            sp.GetRequiredService<IGlobalHubConnection>()));

        services.AddTransient<DepartmentManagementViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services;
    }
}