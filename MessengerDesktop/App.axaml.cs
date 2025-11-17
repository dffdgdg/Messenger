using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MessengerDesktop.Services;
using MessengerDesktop.ViewModels;
using MessengerDesktop.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Avalonia.Styling;

namespace MessengerDesktop
{
    public partial class App : Application
    {
        public new static App Current => (App)Application.Current!;
        public IServiceProvider Services { get; private set; }
        public Storage Storage { get; } = new Storage();

        private const string ThemeSettingsFile = "theme_settings.json";
        private ThemeVariant _themeVariant;

        public ThemeVariant ThemeVariant
        {
            get => _themeVariant;
            set
            {
                if (_themeVariant != value)
                {
                    _themeVariant = value;
                    RequestedThemeVariant = value;
                    SaveThemeSettings();
                }
            }
        }

        public static readonly string ApiUrl =
#if DEBUG
            "https://localhost:7190/";
#else
            "http://localhost:5274/";
#endif

        public App()
        {
            Services = ConfigureServices;
            LoadThemeSettings();
        }

        private static IServiceProvider ConfigureServices
        {
            get
            {
                var services = new ServiceCollection();

                // Сервисы
                services.AddSingleton<ISecureStorageService, SecureStorageService>();
                services.AddSingleton<IApiClientService, ApiClientService>();
                services.AddSingleton<IDialogService, DialogService>();
                services.AddSingleton<AuthService>();

                services.AddSingleton<HttpClient>(sp =>
                {
                    var handler = new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
                        CheckCertificateRevocationList = false,
                        UseProxy = false
                    };

                    return new HttpClient(handler)
                    {
                        BaseAddress = new Uri(ApiUrl),
                        Timeout = TimeSpan.FromSeconds(30)
                    };
                });

                services.AddSingleton<INavigationService, NavigationService>();

                // ViewModels
                services.AddSingleton<MainWindowViewModel>();
                services.AddTransient<MainMenuViewModel>();
                services.AddTransient<LoginViewModel>();
                services.AddTransient<AdminViewModel>();
                services.AddTransient<ChatsViewModel>();
                services.AddTransient<ProfileViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<ChatViewModel>();

                return services.BuildServiceProvider();
            }
        }

        public override void Initialize()
        {
            System.Diagnostics.Debug.WriteLine("App Initialize starting...");
            AvaloniaXamlLoader.Load(this);
            System.Diagnostics.Debug.WriteLine("App Initialize completed");
        }

        public override void OnFrameworkInitializationCompleted()
        {
            System.Diagnostics.Debug.WriteLine("OnFrameworkInitializationCompleted starting...");

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = new MainWindow();
                desktop.MainWindow = mainWindow;

                WindowService.Initialize(mainWindow);
                NotificationService.Initialize(mainWindow);

                var mainWindowViewModel = Services.GetRequiredService<MainWindowViewModel>();
                mainWindow.DataContext = mainWindowViewModel;

                desktop.Exit += Desktop_Exit;
            }

            System.Diagnostics.Debug.WriteLine("OnFrameworkInitializationCompleted completed");
            base.OnFrameworkInitializationCompleted();
        }

        private void LoadThemeSettings()
        {
            try
            {
                if (File.Exists(ThemeSettingsFile))
                {
                    var json = File.ReadAllText(ThemeSettingsFile);
                    var settings = JsonSerializer.Deserialize<ThemeSettings>(json);
                    _themeVariant = settings?.IsDarkTheme == true ? ThemeVariant.Dark : ThemeVariant.Light;
                }
                else
                {
                    _themeVariant = ThemeVariant.Dark;
                }
            }
            catch
            {
                _themeVariant = ThemeVariant.Dark;
            }

            RequestedThemeVariant = _themeVariant;
        }

        private void SaveThemeSettings()
        {
            try
            {
                var settings = new ThemeSettings(_themeVariant == ThemeVariant.Dark);
                var json = JsonSerializer.Serialize(settings);
                File.WriteAllText(ThemeSettingsFile, json);
            }
            catch
            {
            }
        }

        private void Desktop_Exit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
        {
            WindowService.Cleanup();
            NotificationService.Cleanup();
        }

        public void ToggleTheme() => ThemeVariant = ThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
    }

    public record ThemeSettings(bool IsDarkTheme);
}