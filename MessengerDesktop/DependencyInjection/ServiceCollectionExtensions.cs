using MessengerAPI.Services;
using MessengerDesktop.Services;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Auth;
using MessengerDesktop.Services.Navigation;
using MessengerDesktop.Services.Platform;
using MessengerDesktop.Services.Storage;
using MessengerDesktop.ViewModels;
using MessengerDesktop.ViewModels.Factories;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;

namespace MessengerDesktop.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMessengerCoreServices(this IServiceCollection services, string apiBaseUrl)
    {
        // Platform & Storage
        services.AddSingleton<IPlatformService, PlatformService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ISecureStorageService, SecureStorageService>();
        services.AddSingleton<IChatInfoPanelStateStore, ChatInfoPanelStateStore>();

        services.AddSingleton<IFileDownloadService>(sp =>
        {
            var httpClient = sp.GetRequiredService<HttpClient>();
            return new FileDownloadService(httpClient);
        });

        // Notification Service
        services.AddSingleton<INotificationService, NotificationService>();

        // HTTP Client
        services.AddSingleton<HttpClient>(sp =>
        {
            var handler = new HttpClientHandler
            {
#if DEBUG
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
#endif
                CheckCertificateRevocationList = false,
                UseProxy = false
            };

            return new HttpClient(handler)
            {
                BaseAddress = new Uri(apiBaseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
        });

        // Auth
        services.AddSingleton<Services.AuthService>();
        services.AddSingleton<Services.Auth.IAuthService>(sp => sp.GetRequiredService<Services.AuthService>());

        // API
        services.AddSingleton<IApiClientService, ApiClientService>();
        services.AddSingleton<IOnlineUserService, OnlineUserService>();

        // Navigation & Dialogs
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();

        return services;
    }

    public static IServiceCollection AddMessengerViewModels(this IServiceCollection services)
    {
        // ViewModel Factories
        services.AddSingleton<IChatViewModelFactory, ChatViewModelFactory>();
        services.AddSingleton<IChatsViewModelFactory, ChatsViewModelFactory>();

        // Main Window ViewModel
        services.AddSingleton<MainWindowViewModel>();

        // Transient ViewModels
        services.AddTransient<LoginViewModel>();
        services.AddTransient<MainMenuViewModel>();
        services.AddTransient<AdminViewModel>();
        services.AddTransient<ProfileViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services;
    }

    public static IServiceCollection AddMessengerTestServices(this IServiceCollection services)
    {
        return services;
    }
}