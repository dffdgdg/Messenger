using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Core.Data;
using Core.Infrastructure;
using Core.Infrastructure.Extensions;
using Core.Infrastructure.Helpers;
using Core.Infrastructure.Media;
using Core.Services.Features.Call;
using Core.Services.Features.Media.Abstractions;
using Core.Services.Features.Media.Audio;
using Core.Services.Platform.Network;
using Core.Services.Platform.UI;
using Core.ViewModels;
using Desktop.Services.Platform;
using Desktop.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using AvaloniaApp = Avalonia.Application;
using Debug = System.Diagnostics.Debug;
using Environment = System.Environment;

namespace Desktop;

public sealed class App : AvaloniaApp, IDisposable
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true
    };

    private bool _disposed;
    private INotificationService? _notificationService;
    public static bool WasDiscovered { get; private set; }
    public static new App Current => (App)AvaloniaApp.Current!;
    public IServiceProvider Services { get; private set; } = null!;
    public static string ApiUrl { get; private set; } = null!;
    private AuthenticatedImageLoader? _imageLoader;

    public override void Initialize()
    {
        var config = BuildConfiguration();
        ApiUrl = ResolveApiUrl(config);

        AppConfig.ApiUrl = ApiUrl;
        AppConfig.WasDiscovered = WasDiscovered;
        AppConfig.DefaultAvatarUri =
            new Uri("avares://Desktop/Assets/Images/default-avatar.webp");

        AppConfig.SetApiUrlCallback = (url, manual) =>
        {
            ApiUrl = url;
            AppConfig.WasDiscovered = false;
            SaveServerUrlToSettings(url, manual);
        };

        AppConfig.RestartApplicationCallback = () => { /* ... */ };

        AvaloniaXamlLoader.Load(this);

        Services = ConfigureServices();
    }

    private static IConfiguration BuildConfiguration()
    {
        var env = Environment.GetEnvironmentVariable("MESSENGER_ENV");
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true);

        if (!string.IsNullOrWhiteSpace(env))
            builder.AddJsonFile($"appsettings.{env}.json", optional: true);

        return builder.AddEnvironmentVariables("MESSENGER_").Build();
    }

    private static string ResolveApiUrl(IConfiguration configuration)
    {
        var discoveredUrl = DiscoverServerBlocking();
        if (discoveredUrl is not null)
        {
            SaveServerUrlToSettings(discoveredUrl, manual: false);
            WasDiscovered = true;
            Debug.WriteLine($"[App] Сервер найден через UDP: {discoveredUrl}");
            return discoveredUrl;
        }

        WasDiscovered = false;
        Debug.WriteLine("[App] UDP-обнаружение не дало результатов");

        var savedUrl = TryLoadSavedServerUrl();
        if (savedUrl is not null)
        {
            Debug.WriteLine($"[App] Используем резервный сохранённый URL: {savedUrl}");
            return savedUrl;
        }

        var fallback = configuration["ApiUrl"]
            ?? configuration["Api:BaseUrl"]
            ?? "http://localhost:5274/";

        if (!fallback.EndsWith('/'))
            fallback += "/";

        Debug.WriteLine($"[App] Сервер не найден, fallback на {fallback}");
        return fallback;
    }

    private static string? DiscoverServerBlocking()
    {
        try
        {
            using var loggerFactory = LoggerFactory.Create(b => b.AddDebug());
            var logger = loggerFactory.CreateLogger<ServerDiscoveryService>();
            var discovery = new ServerDiscoveryService(logger);
            return Task.Run(() => discovery.DiscoverAsync(3000)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Ошибка обнаружения: {ex.Message}");
            return null;
        }
    }

    private static void SaveServerUrlToSettings(string url, bool manual = false)
    {
        try
        {
            var filePath = Path.Combine(
                AppPaths.GetAppDataDirectory(), "settings.json");

            string json;
            if (File.Exists(filePath))
                json = File.ReadAllText(filePath);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                json = "{}";
            }

            var root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];
            root["server_url"] = JsonSerializer.SerializeToElement(url);
            root["manual_server"] = JsonSerializer.SerializeToElement(manual);

            File.WriteAllText(filePath, JsonSerializer.Serialize(root, IndentedJsonOptions));
            Debug.WriteLine($"[App] Сохранён URL сервера: {url} (manual={manual})");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Не удалось сохранить URL: {ex.Message}");
        }
    }

    private static string? TryLoadSavedServerUrl()
    {
        try
        {
            var filePath = Path.Combine(
                AppPaths.GetAppDataDirectory(), "settings.json");

            if (!File.Exists(filePath)) return null;

            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);

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

        services.AddSingleton<ISecureStorageService, SecureStorageService>();
        services.AddSingleton<IPlatformService, PlatformService>();
        services.AddSingleton<IDrawerService, DesktopDrawerService>();
        services.AddSingleton<OpenAlLifetime>();
        services.AddSingleton<IAudioCaptureDeviceFactory, OpenAlCaptureDeviceFactory>();
        services.AddSingleton<IAudioCaptureDevice, OpenAlCaptureDevice>();
        services.AddSingleton<IAudioPlaybackDevice, OpenAlPlaybackDevice>();
        services.AddSingleton<IAudioRecorderService, AudioRecorderService>();
        services.AddSingleton<ICallAudioService, CallAudioService>();
        services.AddSingleton<IWebRtcPeerConnectionFactory, DesktopWebRtcFactory>();
        services.AddMessengerCoreServices(ApiUrl);
        services.AddMessengerViewModels();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<ILayoutModeProvider, DesktopLayoutModeProvider>();
        services.AddTransient<AccentPickerViewModel>();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = false
        });

        AppConfig.Services = provider;

        AppConfig.LogoutCallback = async () =>
        {
            var mainVm = provider.GetRequiredService<MainWindowViewModel>();
            await mainVm.Logout();
        };

        return provider;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = Services.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;

            ConfigureImageLoader();

            mainWindow.DataContext = Services.GetRequiredService<MainWindowViewModel>();

            desktop.Exit += OnApplicationExit;
            desktop.ShutdownRequested += OnShutdownRequested;
        }

        InitializeTheme();
        _ = InitializeLocalDatabaseAndMaintenanceAsync();

        base.OnFrameworkInitializationCompleted();
    }

    private void InitializeTheme()
    {
        try
        {
            var themeService = Services.GetRequiredService<IThemeService>();

            var lightDict = (ResourceDictionary)AvaloniaXamlLoader.Load(
                new Uri("avares://ВнутрьСеть/Assets/Styles/Themes/LightTheme.axaml"));

            var darkDict = (ResourceDictionary)AvaloniaXamlLoader.Load(
                new Uri("avares://ВнутрьСеть/Assets/Styles/Themes/DarkTheme.axaml"));

            themeService.Initialize(lightDict, darkDict);
            themeService.LoadFromSettings();

            Debug.WriteLine("[App] Theme initialized successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Theme initialization failed: {ex.Message}");
            Debug.WriteLine($"[App] Stack trace: {ex.StackTrace}");

            // Fallback — пытаемся загрузить без словарей
            var themeService = Services.GetRequiredService<IThemeService>();
            themeService.LoadFromSettings();
        }
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

    public static void SetApiUrl(string url, bool manual = false)
    {
        AppConfig.SetApiUrl(url, manual);
        ApiUrl = AppConfig.ApiUrl;
    }

    private void ConfigureImageLoader()
    {
        var httpClient = Services.GetRequiredService<HttpClient>();
        var sessionStore = Services.GetRequiredService<ISessionStore>();
        _ = new AuthenticatedImageLoader(httpClient, sessionStore, ApiUrl);
        _imageLoader = Services.GetRequiredService<AuthenticatedImageLoader>();

        Debug.WriteLine("[App] LimitedCacheImageLoader configured");
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

            _imageLoader?.Dispose();
            _imageLoader = null;

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