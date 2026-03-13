using MessengerDesktop.Services.Cache;
using MessengerShared.Dto.Auth;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.Auth;

public interface IAuthManager
{
    bool IsInitialized { get; }
    ISessionStore Session { get; }
    Task InitializeAsync();
    Task<ApiResponse<AuthResponseDto>> LoginAsync(string username, string password, bool rememberMe);
    Task<ApiResponse<object>> LogoutAsync();
    Task WaitForInitializationAsync();
    Task<bool> WaitForInitializationAsync(TimeSpan timeout);
    Task<bool> TryRefreshTokenAsync();
}

public sealed class AuthManager : IAuthManager, IDisposable
{
    private readonly IAuthService _authService;
    private readonly ISecureStorageService _secureStorage;
    private readonly ICacheMaintenanceService _cacheMaintenance;
    private readonly TaskCompletionSource _initializationTcs = new();
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private Task<bool>? _activeRefreshTask;

    private const string TokenKey = "auth_token";
    private const string RefreshTokenKey = "auth_refresh_token";
    private const string UserIdKey = "user_id";
    private const string UserRoleKey = "user_role";
    private const string CachedUserIdKey = "cached_user_id";
    private const string RememberMeKey = "remember_me";
    private const string SavedUsernameKey = "saved_username";

    private bool _disposed;

    public bool IsInitialized { get; private set; }
    public ISessionStore Session { get; }

    public AuthManager(IAuthService authService, ISecureStorageService secureStorage,
        ISessionStore sessionStore, ICacheMaintenanceService cacheMaintenance)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
        Session = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _cacheMaintenance = cacheMaintenance ?? throw new ArgumentNullException(nameof(cacheMaintenance));

        _ = InitializeInternalAsync();
    }

    private async Task InitializeInternalAsync()
    {
        try
        {
            await LoadStoredSessionAsync();
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"AuthManager: Сетевая ошибка при инициализации (токены сохранены): {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            Debug.WriteLine($"AuthManager: Таймаут при инициализации (токены сохранены): {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AuthManager: Критическая ошибка инициализации: {ex.Message}");
            await ClearStoredAuthAsync();
        }
        finally
        {
            IsInitialized = true;
            _initializationTcs.TrySetResult();
        }
    }

    private async Task LoadStoredSessionAsync()
    {
        var storedToken = await _secureStorage.GetAsync<string>(TokenKey);
        var storedRefreshToken = await _secureStorage.GetAsync<string>(RefreshTokenKey);
        var storedUserId = await _secureStorage.GetAsync<int?>(UserIdKey);
        var storedUserRole = await _secureStorage.GetAsync<UserRole>(UserRoleKey);

        if (string.IsNullOrEmpty(storedToken) || !storedUserId.HasValue || string.IsNullOrEmpty(storedRefreshToken))
        {
            Debug.WriteLine("AuthManager: Нет сохранённой сессии");
            return;
        }

        Debug.WriteLine($"AuthManager: Найден сохранённый токен для пользователя {storedUserId}");

        if (_authService.IsAccessTokenValid(storedToken))
        {
            Debug.WriteLine("AuthManager: Access token действителен (локальная проверка)");
            Session.SetSession(storedToken, storedRefreshToken, storedUserId.Value, storedUserRole);
            return;
        }

        Debug.WriteLine("AuthManager: Access token истёк, пробуем refresh...");

        var refreshResult = await _authService.RefreshTokenAsync(storedToken, storedRefreshToken);

        if (refreshResult.Success && refreshResult.Data != null)
        {
            Debug.WriteLine("AuthManager: Refresh успешен");

            var data = refreshResult.Data;
            await SaveAuthAsync(data.Token, data.RefreshToken, data.UserId, data.Role);
            Session.SetSession(data.Token, data.RefreshToken, data.UserId, data.Role);
        }
        else
        {
            Debug.WriteLine($"AuthManager: Refresh неудачен: {refreshResult.Error}");

            if (IsServerAuthRejection(refreshResult.Error))
            {
                Debug.WriteLine("AuthManager: Сервер отклонил токен, очищаем сохранённые данные");
                await ClearStoredAuthAsync();
            }
            else
            {
                Debug.WriteLine("AuthManager: Предполагаемая сетевая ошибка, токены сохранены для повторной попытки");
            }
        }
    }

    private static bool IsServerAuthRejection(string? error)
    {
        if (string.IsNullOrEmpty(error))
            return false;

        if (error.Contains("401") || error.Contains("403"))
            return true;

        string[] authKeywords =
        [
            "Unauthorized",
            "Forbidden",
            "Недействительный",
            "недействительный",
            "истёк",
            "Истёк",
            "отозван",
            "Отозван",
            "заблокирован",
            "Заблокирован",
            "использован",
            "Использован",
            "Сессия истекла"
        ];

        foreach (var keyword in authKeywords)
        {
            if (error.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public async Task<bool> TryRefreshTokenAsync()
    {
        Task<bool> taskToAwait;

        await _refreshLock.WaitAsync();
        try
        {
            if (_activeRefreshTask != null)
            {
                Debug.WriteLine("AuthManager: Refresh уже выполняется, присоединяемся к текущему");
                taskToAwait = _activeRefreshTask;
            }
            else
            {
                Debug.WriteLine("AuthManager: Запускаем новый refresh");
                _activeRefreshTask = ExecuteRefreshAsync();
                taskToAwait = _activeRefreshTask;
            }
        }
        finally
        {
            _refreshLock.Release();
        }

        return await taskToAwait;
    }

    private async Task<bool> ExecuteRefreshAsync()
    {
        try
        {
            var currentToken = Session.Token;
            var currentRefreshToken = Session.RefreshToken;

            if (string.IsNullOrEmpty(currentToken) || string.IsNullOrEmpty(currentRefreshToken))
            {
                Debug.WriteLine("AuthManager: Нет токенов для refresh");
                return false;
            }

            Debug.WriteLine("AuthManager: Выполняем refresh token...");

            var result = await _authService.RefreshTokenAsync(
                currentToken, currentRefreshToken);

            if (result.Success && result.Data != null)
            {
                var data = result.Data;
                Debug.WriteLine($"AuthManager: Refresh успешен для пользователя {data.UserId}");

                await SaveAuthAsync(data.Token, data.RefreshToken, data.UserId, data.Role);
                Session.UpdateTokens(data.Token, data.RefreshToken);

                return true;
            }

            Debug.WriteLine($"AuthManager: Refresh неудачен: {result.Error}");

            if (IsServerAuthRejection(result.Error))
            {
                await ForceLogoutAsync();
            }

            return false;
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"AuthManager: Сетевая ошибка при refresh: {ex.Message}");
            return false;
        }
        catch (TaskCanceledException ex)
        {
            Debug.WriteLine($"AuthManager: Таймаут при refresh: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AuthManager: Исключение при refresh: {ex.Message}");
            return false;
        }
        finally
        {
            await _refreshLock.WaitAsync();
            try
            {
                _activeRefreshTask = null;
            }
            finally
            {
                _refreshLock.Release();
            }
        }
    }

    public Task InitializeAsync() => WaitForInitializationAsync();

    public async Task<ApiResponse<AuthResponseDto>> LoginAsync(string username, string password, bool rememberMe)
    {
        ThrowIfDisposed();

        await _operationLock.WaitAsync();
        try
        {
            Debug.WriteLine($"AuthManager: Попытка авторизации для {username}");
            var result = await _authService.LoginAsync(username, password);

            if (result.Success && result.Data != null)
            {
                Debug.WriteLine($"AuthManager: Авторизация успешна для пользователя {result.Data.Id}");

                await CheckAndClearCacheOnUserChangeAsync(result.Data.Id);
                await SaveAuthAsync(result.Data.Token, result.Data.RefreshToken, result.Data.Id, result.Data.Role);
                Session.SetSession(result.Data.Token, result.Data.RefreshToken, result.Data.Id, result.Data.Role);

                await _secureStorage.SaveAsync(RememberMeKey, rememberMe);
                if (rememberMe)
                {
                    await _secureStorage.SaveAsync(SavedUsernameKey, username);
                }
                else
                {
                    await _secureStorage.RemoveAsync(SavedUsernameKey);
                }
            }
            else
            {
                Debug.WriteLine($"AuthManager: Ошибка авторизации: {result.Error}");
            }

            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AuthManager: Исключение авторизации: {ex.Message}");
            return new ApiResponse<AuthResponseDto>
            {
                Success = false,
                Error = $"Ошибка входа: {ex.Message}",
                Timestamp = DateTime.UtcNow
            };
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<ApiResponse<object>> LogoutAsync()
    {
        ThrowIfDisposed();

        await _operationLock.WaitAsync();
        try
        {
            Debug.WriteLine($"AuthManager: Logout requested for user {Session.UserId}");

            var token = Session.Token;

            if (!string.IsNullOrEmpty(token))
            {
                try
                {
                    await _authService.RevokeAsync(token);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"AuthManager: Revoke error: {ex.Message}");
                }
            }

            await ClearCacheOnLogoutAsync();

            await ClearStoredAuthAsync();

            var rememberMe = await _secureStorage.GetAsync<bool>(RememberMeKey);
            if (!rememberMe)
            {
                await _secureStorage.RemoveAsync(RememberMeKey);
                await _secureStorage.RemoveAsync(SavedUsernameKey);
            }

            Session.ClearSession();

            return new ApiResponse<object>
            {
                Success = true,
                Message = "Выход выполнен",
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AuthManager: Logout exception: {ex.Message}");
            return new ApiResponse<object>
            {
                Success = false,
                Error = $"Ошибка выхода: {ex.Message}",
                Timestamp = DateTime.UtcNow
            };
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task ForceLogoutAsync()
    {
        Debug.WriteLine("AuthManager: Принудительный logout (refresh token недействителен)");
        await ClearStoredAuthAsync();
        Session.ClearSession();
    }

    private async Task CheckAndClearCacheOnUserChangeAsync(int newUserId)
    {
        try
        {
            var cachedUserId = await _secureStorage.GetAsync<int?>(CachedUserIdKey);

            if (cachedUserId.HasValue && cachedUserId.Value != newUserId)
            {
                Debug.WriteLine($"AuthManager: User changed ({cachedUserId.Value} → {newUserId}), clearing cache");
                await _cacheMaintenance.ClearAllDataAsync();
            }

            await _secureStorage.SaveAsync(CachedUserIdKey, newUserId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AuthManager: Cache user check error: {ex.Message}");
        }
    }

    private async Task ClearCacheOnLogoutAsync()
    {
        try
        {
            await _cacheMaintenance.ClearAllDataAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AuthManager: Cache clear on logout error: {ex.Message}");
        }
    }

    private async Task SaveAuthAsync(string token, string refreshToken, int userId, UserRole role)
    {
        await _secureStorage.SaveAsync(TokenKey, token);
        await _secureStorage.SaveAsync(RefreshTokenKey, refreshToken);
        await _secureStorage.SaveAsync(UserIdKey, userId);
        await _secureStorage.SaveAsync(UserRoleKey, role);
    }

    private async Task ClearStoredAuthAsync()
    {
        await _secureStorage.RemoveAsync(TokenKey);
        await _secureStorage.RemoveAsync(RefreshTokenKey);
        await _secureStorage.RemoveAsync(UserIdKey);
        await _secureStorage.RemoveAsync(UserRoleKey);
    }

    public Task WaitForInitializationAsync()
        => _initializationTcs.Task;

    public async Task<bool> WaitForInitializationAsync(TimeSpan timeout)
    {
        var timeoutTask = Task.Delay(timeout);
        var completedTask = await Task.WhenAny(_initializationTcs.Task, timeoutTask);
        return completedTask == _initializationTcs.Task;
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, nameof(AuthManager));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _operationLock.Dispose();
        _refreshLock.Dispose();
    }
}