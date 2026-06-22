using System.ComponentModel;

namespace Core.Services.Abstractions;

public interface ISettingsService : INotifyPropertyChanged
{
    bool NotificationsEnabled { get; set; }
    bool CanBeFoundInSearch { get; set; }

    T? Get<T>(string key);
    void Set<T>(string key, T value);
    void Remove(string key);

    void ResetUserSettings();
}