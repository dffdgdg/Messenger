namespace Core.Services.Abstractions;

public interface ICookieStorageService
{
    Task RestoreAsync();
    Task PersistAsync();
    Task ClearAsync();
}