using AsyncImageLoader;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MessengerDesktop.Data;
using MessengerDesktop.Infrastructure;
using MessengerDesktop.Infrastructure.ImageLoading;
using MessengerDesktop.Services.Cache;
using MessengerDesktop.Services.Platform;
using MessengerDesktop.Services.UI;
using MessengerDesktop.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;

namespace MessengerDesktop;

public class App : Application, IDisposable
{
    private bool _disposed;
    private INotificationService? _notificationService;

    public new static App Current => (App)Application.Current!;

    public IServiceProvider Services { get; private set; } = null!;

    public const string ApiUrl =
#if DEBUG
        "https://localhost:7190/";
#else
        "http://localhost:5274/";
#endif

    public override void Initialize()
    {
        Debug.WriteLine("[App] Initialize starting...");
        AvaloniaXamlLoader.Load(this);
        Services = ConfigureServices();
        Debug.WriteLine("[App] Initialize completed");
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddMessengerCoreServices(ApiUrl);
        services.AddMessengerViewModels();

        services.AddSingleton<IThemeService, ThemeService>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Debug.WriteLine("[App] OnFrameworkInitializationCompleted starting...");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;

            InitializePlatformServices(mainWindow);
            ConfigureImageLoader();

            // Инициализация БД + maintenance в фоне
            _ = InitializeLocalDatabaseAndMaintenanceAsync();

            var themeService = Services.GetRequiredService<IThemeService>();
            themeService.LoadFromSettings();

            mainWindow.DataContext = Services.GetRequiredService<MainWindowViewModel>();

            desktop.Exit += OnApplicationExit;
            desktop.ShutdownRequested += OnShutdownRequested;
        }

        Debug.WriteLine("[App] OnFrameworkInitializationCompleted completed");
        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeLocalDatabaseAndMaintenanceAsync()
    {
        try
        {
            var localDb = Services.GetRequiredService<LocalDatabase>();
            await localDb.InitializeAsync();
            Debug.WriteLine("[App] Local database initialized successfully");

            var maintenance = Services.GetRequiredService<ICacheMaintenanceService>();
            await maintenance.RunMaintenanceAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Local database/maintenance failed (non-critical): {ex.Message}");
        }
    }

    private void ConfigureImageLoader()
    {
        var httpClient = Services.GetRequiredService<HttpClient>();
        var sessionStore = Services.GetRequiredService<ISessionStore>();

        ImageLoader.AsyncImageLoader = new AuthenticatedImageLoader(
            httpClient,
            sessionStore,
            ApiUrl
        );

        Debug.WriteLine("[App] AuthenticatedImageLoader configured");
    }

    private void InitializePlatformServices(MainWindow mainWindow)
    {
        var platformService = Services.GetRequiredService<IPlatformService>();
        platformService.Initialize(mainWindow);

        var notificationService = Services.GetRequiredService<INotificationService>();
        notificationService.Initialize(mainWindow);
        _notificationService = notificationService;

        Debug.WriteLine("[App] Platform services initialized");
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e) =>
        Debug.WriteLine("[App] Shutdown requested");

    private void OnApplicationExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        Debug.WriteLine("[App] Application exiting...");
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Debug.WriteLine("[App] Disposing resources...");

        try
        {
            _notificationService?.Dispose();
            _notificationService = null;

            var platformService = Services.GetService<IPlatformService>();
            platformService?.Cleanup();

            var mainWindowVm = Services.GetService<MainWindowViewModel>();
            mainWindowVm?.Dispose();

            var apiClient = Services.GetService<IApiClientService>();
            (apiClient as IDisposable)?.Dispose();

            var authManager = Services.GetService<IAuthManager>() as IDisposable;
            authManager?.Dispose();

            var sessionStore = Services.GetService<ISessionStore>() as IDisposable;
            sessionStore?.Dispose();

            var dialogService = Services.GetService<IDialogService>() as IDisposable;
            dialogService?.Dispose();

            var navigationService = Services.GetService<INavigationService>() as IDisposable;
            navigationService?.Dispose();

            var localDb = Services.GetService<LocalDatabase>();
            localDb?.Dispose();

            (Services as IDisposable)?.Dispose();

            Debug.WriteLine("[App] Resources disposed successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Error during disposal: {ex.Message}");
        }

        GC.SuppressFinalize(this);
    }
}