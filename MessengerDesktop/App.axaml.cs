using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using MessengerDesktop.DependencyInjection;
using MessengerDesktop.Services;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Platform;
using MessengerDesktop.Services.Storage;
using MessengerDesktop.ViewModels;
using MessengerDesktop.Views;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace MessengerDesktop
{
    public partial class App : Application, IDisposable
    {
        private bool _disposed;
        private INotificationService? _notificationService;

        public new static App Current => (App)Application.Current!;

        public IServiceProvider Services { get; private set; } = null!;

        public static readonly string ApiUrl =
#if DEBUG
            "https://localhost:7190/";
#else
            "http://localhost:5274/";
#endif

        public override void Initialize()
        {
            System.Diagnostics.Debug.WriteLine("[App] Initialize starting...");
            AvaloniaXamlLoader.Load(this);
            Services = ConfigureServices();
            System.Diagnostics.Debug.WriteLine("[App] Initialize completed");
        }

        private static ServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            services.AddMessengerCoreServices(ApiUrl);
            services.AddMessengerViewModels();

            return services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
        }

        public override void OnFrameworkInitializationCompleted()
        {
            System.Diagnostics.Debug.WriteLine("[App] OnFrameworkInitializationCompleted starting...");

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = new MainWindow();
                desktop.MainWindow = mainWindow;

                InitializePlatformServices(mainWindow);
                LoadThemeFromSettings();

                var mainWindowViewModel = Services.GetRequiredService<MainWindowViewModel>();
                mainWindow.DataContext = mainWindowViewModel;

                desktop.Exit += OnApplicationExit;
                desktop.ShutdownRequested += OnShutdownRequested;
            }

            System.Diagnostics.Debug.WriteLine("[App] OnFrameworkInitializationCompleted completed");
            base.OnFrameworkInitializationCompleted();
        }

        private void InitializePlatformServices(MainWindow mainWindow)
        {
            var platformService = Services.GetRequiredService<IPlatformService>();
            platformService.Initialize(mainWindow);

            var notificationService = Services.GetRequiredService<INotificationService>();
            notificationService.Initialize(mainWindow);
            _notificationService = notificationService;

            System.Diagnostics.Debug.WriteLine("[App] Platform services initialized");
        }

        private void LoadThemeFromSettings()
        {
            try
            {
                var settingsService = Services.GetRequiredService<ISettingsService>();
                var isDarkTheme = settingsService.Get<bool?>("IsDarkTheme") ?? true;
                RequestedThemeVariant = isDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;

                System.Diagnostics.Debug.WriteLine($"[App] Theme loaded: {(isDarkTheme ? "Dark" : "Light")}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] Error loading theme: {ex.Message}");
                RequestedThemeVariant = ThemeVariant.Dark;
            }
        }

        public void ToggleTheme()
        {
            var newTheme = RequestedThemeVariant == ThemeVariant.Dark
                ? ThemeVariant.Light
                : ThemeVariant.Dark;

            RequestedThemeVariant = newTheme;

            try
            {
                var settingsService = Services.GetRequiredService<ISettingsService>();
                settingsService.Set("IsDarkTheme", newTheme == ThemeVariant.Dark);

                System.Diagnostics.Debug.WriteLine($"[App] Theme toggled to: {newTheme}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] Error saving theme: {ex.Message}");
            }
        }

        public ThemeVariant ThemeVariant
        {
            get => RequestedThemeVariant ?? ThemeVariant.Dark;
            set
            {
                if (RequestedThemeVariant != value)
                {
                    RequestedThemeVariant = value;

                    try
                    {
                        var settingsService = Services.GetRequiredService<ISettingsService>();
                        settingsService.Set("IsDarkTheme", value == ThemeVariant.Dark);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[App] Error saving theme variant: {ex.Message}");
                    }
                }
            }
        }

        private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[App] Shutdown requested");
        }

        private void OnApplicationExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[App] Application exiting...");
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            System.Diagnostics.Debug.WriteLine("[App] Disposing resources...");

            try
            {
                _notificationService = null;

                var platformService = Services.GetService<IPlatformService>();
                platformService?.Cleanup();

                var mainWindowVm = Services.GetService<MainWindowViewModel>();
                mainWindowVm?.Dispose();

                var apiClient = Services.GetService<IApiClientService>();
                (apiClient as IDisposable)?.Dispose();

                var authService = Services.GetService<AuthService>();

                (Services as IDisposable)?.Dispose();

                System.Diagnostics.Debug.WriteLine("[App] Resources disposed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] Error during disposal: {ex.Message}");
            }

            GC.SuppressFinalize(this);
        }
    }
}