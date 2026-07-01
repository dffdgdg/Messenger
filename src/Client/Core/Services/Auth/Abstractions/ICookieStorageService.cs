namespace Core.Services.Auth.Abstractions;

public interface ICookieStorageService
{
    Task RestoreAsync();
    Task PersistAsync();
    Task ClearAsync();
}