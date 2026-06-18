namespace Core.Infrastructure.Helpers;

public static class AppPaths
{
    public static string GetDatabasePath(string fileName)
    {
        var dir = GetAppDataDirectory();
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, fileName);
    }

    public static string GetAppDataDirectory()
    {
#if ANDROID
        var context = Android.App.Application.Context;
        return context.FilesDir!.AbsolutePath;
#else
        var appData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "Desktop");
#endif
    }
}