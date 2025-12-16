using System;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.Auth;

public interface IAuthService
{
    int? UserId { get; }
    string? Token { get; }
    bool IsAuthenticated { get; }
    bool IsInitialized { get; }

    Task<bool> LoginAsync(string username, string password);
    Task ClearAuthAsync();
    Task WaitForInitializationAsync();
    Task<bool> WaitForInitializationAsync(TimeSpan timeout);
}