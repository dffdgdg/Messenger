namespace Core.Services.Abstractions;

public interface ISecureStorageService
{
    Task SaveAsync<T>(string key, T value);
    Task<T?> GetAsync<T>(string key);
    Task RemoveAsync(string key);
    Task<bool> ContainsKeyAsync(string key);
}