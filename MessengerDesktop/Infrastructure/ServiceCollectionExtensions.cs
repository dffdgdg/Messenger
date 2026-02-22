using MessengerDesktop.Data;
using MessengerDesktop.Data.Repositories;
using MessengerDesktop.Services;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Audio;
using MessengerDesktop.Services.Auth;
using MessengerDesktop.Services.Cache;
using MessengerDesktop.Services.Navigation;
using MessengerDesktop.Services.Platform;
using MessengerDesktop.Services.Realtime;
using MessengerDesktop.Services.Storage;
using MessengerDesktop.Services.UI;
using MessengerDesktop.ViewModels;
using MessengerDesktop.ViewModels.Department;
using MessengerDesktop.ViewModels.Factories;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Net.Http;

namespace MessengerDesktop.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMessengerCoreServices(this IServiceCollection services, string apiBaseUrl)
    {
        services.AddSingleton<LocalDatabase>(_ =>
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

        services.AddSingleton(_ =>
        {
            var handler = new HttpClientHandler
            {
                CheckCertificateRevocationList = false,
                UseProxy = false
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

        services.AddSingleton<IApiClientService>(sp =>
        {
            var httpClient = sp.GetRequiredService<HttpClient>();
            var sessionStore = sp.GetRequiredService<ISessionStore>();
            return new ApiClientService(httpClient, sessionStore);
        });

        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<INotificationService, NotificationService>();

        services.AddSingleton<IFileDownloadService>(sp =>
        {
            var httpClient = sp.GetRequiredService<HttpClient>();
            return new FileDownloadService(httpClient);
        });
        services.AddSingleton<IAudioRecorderService, NAudioRecorderService>();

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
        services.AddTransient<ProfileViewModel>();
        services.AddTransient<DepartmentManagementViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services;
    }
}