using MessengerShared.Dto.Auth;
using System;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.Abstractions;

public interface IAuthManager
{
    bool IsInitialized { get; }
    ISessionStore Session { get; }
    bool HasValidSession();
    Task InitializeAsync();
    Task<ApiResponse<AuthResponseDto>> LoginAsync(string username, string password, bool rememberMe);
    Task<ApiResponse<object>> LogoutAsync();
    Task WaitForInitializationAsync();
    Task<bool> WaitForInitializationAsync(TimeSpan timeout);
    Task<bool> TryRefreshTokenAsync();
}