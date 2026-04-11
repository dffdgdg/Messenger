using System;
using System.Globalization;

namespace MessengerDesktop;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var culture = new CultureInfo("ru-RU");

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}