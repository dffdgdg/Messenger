using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MessengerDesktop.Services;

public class Storage
{
    private readonly Dictionary<string, object> _storage = [];
    private readonly string _filePath;
    private bool _isLoaded;

    public Storage()
    {
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
          "MessengerDesktop",
        "storage.json"
        );
        Load();
    }

    public void Set(string key, object value)
    {
        _storage[key] = value;
        Save();
    }

    public object? Get(string key)
    {
        EnsureLoaded();
        return _storage.TryGetValue(key, out var value) ? value : null;
    }

    public void Remove(string key)
    {
        if (_storage.Remove(key))
        {
            Save();
        }
    }

    private void EnsureLoaded()
    {
        if (!_isLoaded)
        {
            Load();
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>
                                   (json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (data != null)
                {
                    _storage.Clear();
                    foreach (var kvp in data)
                    {
                        _storage[kvp.Key] = kvp.Value;
                    }
                }
            }
        }
        catch
        {
            _storage.Clear();
        }
        finally
        {
            _isLoaded = true;
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (directory != null)
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_storage);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
        }

    }
}