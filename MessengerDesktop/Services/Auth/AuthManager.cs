using MessengerShared.DTO.Auth;
using MessengerShared.Enum;
using MessengerShared.Response;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.Auth;

public interface IAuthManager
{
    bool IsInitialized { get; }
    ISessionStore Session { get; }
    Task InitializeAsync();
    Task<ApiResponse<AuthResponseDTO>> LoginAsync(string username, string password);
    Task<ApiResponse> LogoutAsync();
    Task WaitForInitializationAsync();
    Task<bool> WaitForInitializationAsync(TimeSpan timeout);
}

public class AuthManager : IAuthManager, IDisposable
{
    private readonly IAuthService _authService;
    private readonly ISecureStorageService _secureStorage;
    private readonly TaskCompletionSource _initializationTcs = new();
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    private const string TokenKey = "auth_token";
    private const string UserIdKey = "user_id";
    private const string UserRoleKey = "user_role";

    private Task? _initializationTask;
    private bool _disposed;

    public bool IsInitialized { get; private set; }
    public ISessionStore Session { get; }

    public AuthManager(IAuthService authService,ISecureStorageService secureStorage,ISessionStore sessionStore)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
        Session = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _initializationTask = InitializeInternalAsync();

        _ = InitializeInternalAsync();
    }

    private async Task<bool> ValidateStoredTokenAsync(string token)
    {
        try
        {
            var validationResult = await _authService.ValidateTokenAsync(token);

            if (validationResult.Success)
            {
                Debug.WriteLine("AuthManager: Токен действителен");
                return true;
            }
            else
            {
                Debug.WriteLine($"AuthManager: Токен недействителен: {validationResult.Error}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AuthManager: Ошибка проверки токена: {ex.Message}");
            return false;
        }
    }

    private async Task InitializeInternalAsync()
    {
        try
        {
            await LoadStoredSessionAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AuthManager initialization error: {ex.Message}");
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
        var storedUserId = await _secureStorage.GetAsync<int?>(UserIdKey);
        var storedUserRole = await _secureStorage.GetAsync<UserRole>(UserRoleKey);

        if (!string.IsNullOrEmpty(storedToken) && storedUserId.HasValue)
        {
            Debug.WriteLine($"AuthManager: Найден сохранённый токен для пользователя {storedUserId}");

            var isValid = await ValidateStoredTokenAsync(storedToken);

            if (isValid)
            {
                Debug.WriteLine("AuthManager: Токен действителен, устанавливаю сессию");
                Session.SetSession(storedToken, storedUserId.Value, storedUserRole);
            }
            else
            {
                Debug.WriteLine("AuthManager: Токен недействителен, очищаю");
                await ClearStoredAuthAsync();
            }
        }
        else
        {
            Debug.WriteLine("AuthManager: Нет сохранённой сессии");
        }
    }

    public Task InitializeAsync() => WaitForInitializationAsync();

    public async Task<ApiResponse<AuthResponseDTO>> LoginAsync(string username, string password)
    {
        ThrowIfDisposed();

        await _operationLock.WaitAsync();
        try
        {
            Debug.WriteLine($"AuthManager: Login attempt for {username}");
            var result = await _authService.LoginAsync(username, password);

            if (result.Success && result.Data != null)
            {
                Debug.WriteLine($"AuthManager: Login successful for user {result.Data.Id}");
                await SaveAuthAsync(result.Data.Token, result.Data.Id, result.Data.Role);
                Session.SetSession(result.Data.Token, result.Data.Id, result.Data.Role);
            }
            else
            {
                Debug.WriteLine($"AuthManager: Login failed: {result.Error}");
            }

            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AuthManager: Login exception: {ex.Message}");
            return new ApiResponse<AuthResponseDTO>
            {
                Success = false,
                Error = $"Ошибка входа: {ex.Message}",
                Timestamp = DateTime.Now
            };
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<ApiResponse> LogoutAsync()
    {
        ThrowIfDisposed();

        await _operationLock.WaitAsync();
        try
        {
            Debug.WriteLine($"AuthManager: Logout requested for user {Session.UserId}");

            var token = Session.Token;

            if (!string.IsNullOrEmpty(token))
            {
                var result = await _authService.LogoutAsync(token);
            }

            await ClearStoredAuthAsync();
            Session.ClearSession();

            return new ApiResponse
            {
                Success = true,
                Message = "Выход выполнен",
                Timestamp = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AuthManager: Logout exception: {ex.Message}");
            return new ApiResponse
            {
                Success = false,
                Error = $"Ошибка выхода: {ex.Message}",
                Timestamp = DateTime.Now
            };
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task SaveAuthAsync(string token, int userId, UserRole role)
    {
        await _secureStorage.SaveAsync(TokenKey, token);
        await _secureStorage.SaveAsync(UserIdKey, userId);
        await _secureStorage.SaveAsync(UserRoleKey, role);
    }

    private async Task ClearStoredAuthAsync()
    {
        await _secureStorage.RemoveAsync(TokenKey);
        await _secureStorage.RemoveAsync(UserIdKey);
        await _secureStorage.RemoveAsync(UserRoleKey);

        Debug.WriteLine("AuthManager: Stored auth cleared");
    }

    public Task WaitForInitializationAsync() => _initializationTcs.Task;

    public async Task<bool> WaitForInitializationAsync(TimeSpan timeout)
    {
        var timeoutTask = Task.Delay(timeout);
        var completedTask = await Task.WhenAny(_initializationTcs.Task, timeoutTask);
        return completedTask == _initializationTcs.Task;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, nameof(AuthManager));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _operationLock.Dispose();

        GC.SuppressFinalize(this);
    }
}