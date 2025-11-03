using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MessengerDesktop.ViewModels;
using MessengerDesktop.Views;
using MessengerDesktop.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;
using Avalonia.Styling;
using System.IO;
using System.Text.Json;

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

                // Сначала регистрируем сервисы без зависимостей
                services.AddSingleton<AuthService>();
                services.AddSingleton<INavigationService, NavigationService>();

                // HttpClient регистрируем через фабрику
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
                    // Токен будет добавляться позже, после аутентификации
                });

                // ViewModels
                services.AddSingleton<MainWindowViewModel>();
                services.AddTransient<MainMenuViewModel>();
                services.AddTransient<LoginViewModel>();
                services.AddTransient<AdminViewModel>();
                services.AddTransient<ChatsViewModel>();
                services.AddTransient<ProfileViewModel>();
                services.AddTransient<SettingsViewModel>();

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

                // Инициализируем сервисы
                WindowService.Initialize(mainWindow);
                NotificationService.Initialize(mainWindow);

                // Получаем ViewModel через сервисы
                var mainWindowViewModel = Services.GetRequiredService<MainWindowViewModel>();
                mainWindow.DataContext = mainWindowViewModel;

                // Обработчик выхода
                desktop.Exit += Desktop_Exit;
            }

            System.Diagnostics.Debug.WriteLine("OnFrameworkInitializationCompleted completed");
            base.OnFrameworkInitializationCompleted();
        }

        public static string? GetCurrentToken()
        {
            try
            {
                var authService = Current?.Services.GetRequiredService<AuthService>();
                return authService?.Token;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetCurrentToken error: {ex.Message}");
                return null;
            }
        }

        public static void UpdateHttpClientToken(string? token)
        {
            try
            {
                var httpClient = Current?.Services.GetRequiredService<HttpClient>();
                if (httpClient != null)
                {
                    // Очищаем старые заголовки
                    httpClient.DefaultRequestHeaders.Remove("Authorization");

                    // Добавляем новый токен
                    if (!string.IsNullOrEmpty(token))
                    {
                        httpClient.DefaultRequestHeaders.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                        System.Diagnostics.Debug.WriteLine("HttpClient token updated successfully");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("HttpClient token cleared");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateHttpClientToken error: {ex.Message}");
            }
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