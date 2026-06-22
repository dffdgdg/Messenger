using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Core.Infrastructure.Configuration;
using Core.Infrastructure.Extensions;
using Core.Services.Abstractions;
using Core.Services.Platform.Network;
using Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mobile.Services.Platform;
using Mobile.Views;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Mobile;

public partial class App : Application
{
    public static new App Current => (App)Application.Current!;
    public static Action<IServiceCollection>? RegisterPlatformServices { get; set; }
    public IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        // Initialize должен быть максимально простым
        // Никаких Task.Run, никаких сетевых вызовов
        AppConfig.DefaultAvatarUri = new Uri("avares://Mobile/Assets/avalonia-logo.ico");
        AppConfig.SetApiUrlCallback = (url, manual) =>
        {
            AppConfig.ApiUrl = url;
            Debug.WriteLine($"[App] ApiUrl изменён: {url}");
        };

        AvaloniaXamlLoader.Load(this);

        // Конфигурируем сервисы с fallback URL
        // Реальный URL подберём асинхронно после старта
        const string fallback = "http://10.0.2.2:5274/";
        AppConfig.ApiUrl = fallback;
        Services = ConfigureServices(fallback);

        Debug.WriteLine("[App] Initialize completed");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            Debug.WriteLine($"[App] Lifetime: {ApplicationLifetime?.GetType().Name}");

            if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            {
                var mainView = Services.GetRequiredService<MainView>();
                mainView.DataContext = Services.GetRequiredService<MainWindowViewModel>();
                singleView.MainView = mainView;
            }
            else if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainView = Services.GetRequiredService<MainView>();
                desktop.MainWindow = new Window
                {
                    Content = mainView,
                    Width = 400,
                    Height = 800,
                    Title = "ВнутрьСеть Mobile"
                };
                mainView.DataContext = Services.GetRequiredService<MainWindowViewModel>();
            }

            base.OnFrameworkInitializationCompleted();

            _ = InitializeAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] OnFrameworkInitializationCompleted error: {ex}");
            throw;
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            // 1. База данных
            var db = Services.GetRequiredService<Core.Data.LocalDatabase>();
            await db.InitializeAsync();

            var maintenance = Services.GetRequiredService<ICacheMaintenanceService>();
            await maintenance.RunMaintenanceAsync();

            Debug.WriteLine("[App] Database initialized");

            // 2. Обнаружение сервера (асинхронно, не блокирует UI)
            await DiscoverServerAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] InitializeAsync error: {ex}");
        }
    }

    private async Task DiscoverServerAsync()
    {
        try
        {
            using var loggerFactory = LoggerFactory.Create(b => b.AddDebug());
            var logger = loggerFactory.CreateLogger<ServerDiscoveryService>();
            var discovery = new ServerDiscoveryService(logger);

            // CancellationToken с таймаутом вместо Task.Run
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var discovered = await discovery.DiscoverAsync(2000);

            if (discovered != null)
            {
                Debug.WriteLine($"[App] Сервер найден: {discovered}");
                AppConfig.ApiUrl = discovered;
                // Уведомляем сервисы об изменении URL если нужно
                AppConfig.SetApiUrlCallback?.Invoke(discovered, false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] UDP discovery error: {ex.Message}");
        }
    }

    private static ServiceProvider ConfigureServices(string apiUrl)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug());
        services.AddMessengerCoreServices(apiUrl);
        services.AddMessengerViewModels();
        services.AddSingleton<MainView>();
        services.AddSingleton<ILayoutModeProvider, MobileLayoutModeProvider>();

        RegisterPlatformServices?.Invoke(services);

        var provider = services.BuildServiceProvider();
        AppConfig.Services = provider;
        AppConfig.LogoutCallback = async () =>
        {
            var mainVm = provider.GetRequiredService<MainWindowViewModel>();
            await mainVm.Logout();
        };

        return provider;
    }
}