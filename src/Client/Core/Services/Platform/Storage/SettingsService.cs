using System.ComponentModel;
using System.Text.Json;
using Core.Services.Platform.Abstractions;
using Core.Shared.Helpers;

namespace Core.Services.Platform.Storage;

public class SettingsService : ISettingsService
{
    private readonly string _filePath;
    private Dictionary<string, JsonElement> _localStorage = [];

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private bool _notificationsEnabled = true;
    private bool _canBeFoundInSearch = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsService()
    {
        var appFolder = AppPaths.GetAppDataDirectory();
        Directory.CreateDirectory(appFolder);
        _filePath = Path.Combine(appFolder, "settings.json");

        LoadLocalStorage();
    }

    #region User Settings

    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set
        {
            if (_notificationsEnabled == value) return;
            _notificationsEnabled = value;
            OnPropertyChanged(nameof(NotificationsEnabled));
        }
    }

    public bool CanBeFoundInSearch
    {
        get => _canBeFoundInSearch;
        set
        {
            if (_canBeFoundInSearch == value) return;
            _canBeFoundInSearch = value;
            OnPropertyChanged(nameof(CanBeFoundInSearch));
        }
    }

    public void ResetUserSettings()
    {
        NotificationsEnabled = true;
        CanBeFoundInSearch = true;
    }

    #endregion

    #region Local Storage

    public T? Get<T>(string key)
    {
        if (!_localStorage.TryGetValue(key, out var element))
            return default;

        try
        {
            return element.Deserialize<T>();
        }
        catch
        {
            return default;
        }
    }

    public void Set<T>(string key, T value)
    {
        _localStorage[key] = JsonSerializer.SerializeToElement(value);
        SaveLocalStorage();
    }

    public void Remove(string key)
    {
        if (_localStorage.Remove(key))
            SaveLocalStorage();
    }

    #endregion

    #region Persistence

    private void LoadLocalStorage()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                _localStorage = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];
            }
        }
        catch
        {
            _localStorage = [];
        }
    }

    private void SaveLocalStorage()
    {
        try
        {
            var json = JsonSerializer.Serialize(_localStorage, _jsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch { }
    }

    #endregion

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}