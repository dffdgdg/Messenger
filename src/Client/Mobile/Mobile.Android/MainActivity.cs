using Android.Content.PM;
using Android.Util;
using Avalonia.Android;

namespace Mobile.Android;

[Activity(Label = "ВнутрьСеть", Theme = "@style/MyTheme.NoActionBar", Icon = "@drawable/icon", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            Log.Error("VNUTRSET", $"UnhandledException: {ex}");
        };

        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            Log.Error("VNUTRSET", $"UnobservedTask: {args.Exception}");
            args.SetObserved();
        };

        try
        {
            base.OnCreate(savedInstanceState);
        }
        catch (Exception ex)
        {
            Log.Error("VNUTRSET", $"OnCreate FAILED: {ex}");
            Log.Error("VNUTRSET", $"Inner: {ex.InnerException}");
            throw;
        }
    }
}