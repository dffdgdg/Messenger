using MessengerDesktop.Services;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Auth;
using MessengerDesktop.Services.Navigation;
using MessengerDesktop.Services.Platform;
using MessengerDesktop.Services.Storage;
using MessengerDesktop.Services.UI;
using MessengerDesktop.ViewModels;
using MessengerDesktop.ViewModels.Department;
using MessengerDesktop.ViewModels.Factories;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;

namespace MessengerDesktop.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddMessengerCoreServices(this IServiceCollection services, string apiBaseUrl)
        {
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
}