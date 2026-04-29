using AsyncImageLoader;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MessengerDesktop.Data;
using MessengerDesktop.Infrastructure.Extensions;
using MessengerDesktop.Infrastructure.Media;
using MessengerDesktop.Services.Infrastructure.UI;
using MessengerDesktop.ViewModels;
using MessengerDesktop.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;

namespace MessengerDesktop;

public sealed class App : Application, IDisposable
{
    private bool _disposed;
    private INotificationService? _notificationService;

    public static new App Current => (App)Application.Current!;

    public IServiceProvider Services { get; private set; } = null!;

    public static string ApiUrl { get; private set; } = null!;

    public override void Initialize()
    {
        var config = BuildConfiguration();
        ApiUrl = ResolveApiUrl(config);
        Debug.WriteLine($"[App] ApiUrl = {ApiUrl}");
        AvaloniaXamlLoader.Load(this);
        Services = ConfigureServices();
    }

    private static IConfiguration BuildConfiguration()
    {
        var env = Environment.GetEnvironmentVariable("MESSENGER_ENV");
        var builder = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: true);
        if (!string.IsNullOrWhiteSpace(env))
            builder.AddJsonFile($"appsettings.{env}.json", optional: true);

        return builder.AddEnvironmentVariables("MESSENGER_").Build();
    }

    private static string ResolveApiUrl(IConfiguration configuration)
    {
        var savedUrl = TryLoadSavedServerUrl();
        if (savedUrl is not null)
            return savedUrl;

        var apiUrl = configuration["ApiUrl"] ?? configuration["Api:BaseUrl"] ?? "http://localhost:5274/";

        if (!apiUrl.EndsWith('/'))
            apiUrl += "/";

        return apiUrl;
    }
    private static string? TryLoadSavedServerUrl()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var filePath = System.IO.Path.Combine(appData, "MessengerDesktop", "settings.json");

            if (!System.IO.File.Exists(filePath))
                return null;

            var json = System.IO.File.ReadAllText(filePath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("server_url", out var el))
            {
                var url = el.GetString();
                if (!string.IsNullOrWhiteSpace(url))
                {
                    Debug.WriteLine($"[App] Загружен сохранённый URL: {url}");
                    return url;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Не удалось загрузить сохранённый URL: {ex.Message}");
        }

        return null;
    }
    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddDebug().AddConsole());

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

            ConfigureImageLoader();

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

        ImageLoader.AsyncImageLoader = new AuthenticatedImageLoader(httpClient, sessionStore, ApiUrl);

        Debug.WriteLine("[App] AuthenticatedImageLoader configured");
    }

    private static void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e) =>
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
    }
}