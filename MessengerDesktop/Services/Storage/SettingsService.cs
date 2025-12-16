using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.Storage
{
    public interface ISettingsService
    {
        Task<T?> GetAsync<T>(string key);
        T? Get<T>(string key);
        Task SetAsync<T>(string key, T value);
        void Set<T>(string key, T value);
        Task RemoveAsync(string key);
        void Remove(string key);
        bool ContainsKey(string key);
    }

    public class SettingsService : ISettingsService
    {
        private readonly string _filePath;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly JsonSerializerOptions _jsonOptions;

        private Dictionary<string, JsonElement> _cache = [];
        private bool _isLoaded;

        public SettingsService()
        {
            _filePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MessengerDesktop",
                "settings.json");

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            LoadSync();
        }

        public async Task<T?> GetAsync<T>(string key)
        {
            await _lock.WaitAsync();
            try
            {
                EnsureLoaded();
                return GetInternal<T>(key);
            }
            finally
            {
                _lock.Release();
            }
        }

        public T? Get<T>(string key)
        {
            _lock.Wait();
            try
            {
                EnsureLoaded();
                return GetInternal<T>(key);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task SetAsync<T>(string key, T value)
        {
            await _lock.WaitAsync();
            try
            {
                EnsureLoaded();
                SetInternal(key, value);
                await SaveAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingsService.SetAsync error: {ex.Message}");
            }
            finally
            {
                _lock.Release();
            }
        }

        public void Set<T>(string key, T value)
        {
            _lock.Wait();
            try
            {
                EnsureLoaded();
                SetInternal(key, value);
                SaveSync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingsService.Set error: {ex.Message}");
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task RemoveAsync(string key)
        {
            await _lock.WaitAsync();
            try
            {
                EnsureLoaded();
                if (_cache.Remove(key))
                    await SaveAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingsService.RemoveAsync error: {ex.Message}");
            }
            finally
            {
                _lock.Release();
            }
        }

        public void Remove(string key)
        {
            _lock.Wait();
            try
            {
                EnsureLoaded();
                if (_cache.Remove(key))
                    SaveSync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingsService.Remove error: {ex.Message}");
            }
            finally
            {
                _lock.Release();
            }
        }

        public bool ContainsKey(string key)
        {
            _lock.Wait();
            try
            {
                EnsureLoaded();
                return _cache.ContainsKey(key);
            }
            finally
            {
                _lock.Release();
            }
        }

        private T? GetInternal<T>(string key)
        {
            if (!_cache.TryGetValue(key, out var element))
                return default;

            try
            {
                if (typeof(T) == typeof(bool) && element.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    return (T)(object)(element.ValueKind == JsonValueKind.True);

                if (typeof(T) == typeof(string) && element.ValueKind == JsonValueKind.String)
                    return (T)(object)element.GetString()!;

                if (typeof(T) == typeof(int) && element.ValueKind == JsonValueKind.Number)
                    return (T)(object)element.GetInt32();

                if (typeof(T) == typeof(long) && element.ValueKind == JsonValueKind.Number)
                    return (T)(object)element.GetInt64();

                if (typeof(T) == typeof(double) && element.ValueKind == JsonValueKind.Number)
                    return (T)(object)element.GetDouble();

                return element.Deserialize<T>(_jsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingsService.GetInternal deserialization error for key '{key}': {ex.Message}");
                return default;
            }
        }

        private void SetInternal<T>(string key, T value)
        {
            if (value is null)
            {
                _cache.Remove(key);
                return;
            }

            _cache[key] = JsonSerializer.SerializeToElement(value, _jsonOptions);
        }

        private void EnsureLoaded()
        {
            if (!_isLoaded)
                LoadSync();
        }

        private void LoadSync()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, _jsonOptions);
                        if (data is not null)
                        {
                            _cache = data;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingsService.LoadSync error: {ex.Message}");
                _cache = [];
            }
            finally
            {
                _isLoaded = true;
            }
        }

        private async Task SaveAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_cache, _jsonOptions);
                await File.WriteAllTextAsync(_filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingsService.SaveAsync error: {ex.Message}");
            }
        }

        private void SaveSync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_cache, _jsonOptions);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingsService.SaveSync error: {ex.Message}");
            }
        }
    }
}