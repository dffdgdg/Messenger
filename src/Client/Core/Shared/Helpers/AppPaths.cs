namespace Core.Shared.Helpers;

public static class AppPaths
{
    public static string GetDatabasePath(string fileName)
    {
        var dir = GetAppDataDirectory();
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, fileName);
    }

    /// <summary>
    /// Приватный кэш скачанных вложений. Имя файла в нём всегда
    /// детерминировано по Id файла на сервере — это устраняет гонку
    /// в генерации уникальных имён и делает докачку (.part) возможной.
    /// </summary>
    public static string GetFileCacheDirectory()
    {
        var dir = Path.Combine(GetAppDataDirectory(), "FilesCache");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetAppDataDirectory()
    {
#if ANDROID
        var context = Android.App.Application.Context;
        return context.FilesDir!.AbsolutePath;
#else
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "Desktop");
#endif
    }
}