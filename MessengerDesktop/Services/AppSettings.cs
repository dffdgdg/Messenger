using System.IO;
using System.Text.Json;
using System;
using System.Collections.Generic;

namespace MessengerDesktop.Services
{
    public static class AppSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MessengerDesktop",
            "settings.json"
        );

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true
        };

        static AppSettings()
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory!);
            }

            if (!File.Exists(SettingsPath))
            {
                File.WriteAllText(SettingsPath, "{}");
            }
        }

        public static string? GetSetting(string key)
        {
            try
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(json);
                return settings?.GetValueOrDefault(key);
            }
            catch
            {
                return null;
            }
        }

        public async static void SetSetting(string key, string value)
        {
            try
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(json) ?? [];

                settings[key] = value;
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, _jsonOptions));
            }
            catch (Exception ex)
            {
                await NotificationService.ShowError($"Ошибка сохранения настроек: {ex.Message}");
            }
        }

        public async static void RemoveSetting(string key)
        {
            try
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(json) ?? [];

                settings.Remove(key);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, _jsonOptions));
            }
            catch (Exception ex)
            {
                await NotificationService.ShowError($"Ошибка удаления настроек: {ex.Message}");
            }
        }
    }
}