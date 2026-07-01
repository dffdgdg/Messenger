using Core.Services.Auth.Abstractions;
using Core.Services.Platform.Abstractions;
using System.Diagnostics;
using System.Net;

namespace Core.Services.Auth;

/// <summary>
/// Сохраняет refresh_token cookie между запусками приложения.
/// CookieContainer живёт только в памяти — этот сервис делает его персистентным.
/// </summary>
public sealed class CookieStorageService(ISecureStorageService secureStorage, CookieContainer cookieContainer,
    string apiBaseUrl) : ICookieStorageService
{
    private readonly string _apiBaseUrl = apiBaseUrl.TrimEnd('/');

    private const string CookieKey = "http_refresh_cookie";
    private const string RefreshTokenCookieName = "refresh_token";

    /// <summary>
    /// Вызывать при старте приложения — восстанавливает cookie из SecureStorage в CookieContainer.
    /// </summary>
    public async Task RestoreAsync()
    {
        try
        {
            var saved = await secureStorage.GetAsync<SavedCookie>(CookieKey);
            if (saved is null)
            {
                Debug.WriteLine("CookieStorage: Нет сохранённого cookie");
                return;
            }

            if (saved.Expires < DateTime.UtcNow)
            {
                Debug.WriteLine("CookieStorage: Cookie истёк, удаляем");
                await secureStorage.RemoveAsync(CookieKey);
                return;
            }

            var uri = new Uri($"{_apiBaseUrl}/api/auth/");
            var cookie = new Cookie(RefreshTokenCookieName, saved.Value)
            {
                Domain = uri.Host,
                Path = "/api/auth",
                HttpOnly = true,
                Secure = uri.Scheme == "https",
                Expires = saved.Expires
            };

            cookieContainer.Add(uri, cookie);
            Debug.WriteLine($"CookieStorage: Cookie восстановлен, истекает {saved.Expires:u}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CookieStorage: Ошибка восстановления: {ex.Message}");
        }
    }

    /// <summary>
    /// Сохраняет текущий refresh_token cookie из CookieContainer в SecureStorage.
    /// Вызывать после успешного Login и Refresh.
    /// </summary>
    public async Task PersistAsync()
    {
        try
        {
            var uri = new Uri($"{_apiBaseUrl}/api/auth/refresh");
            var cookies = cookieContainer.GetCookies(uri);
            var refreshCookie = cookies[RefreshTokenCookieName];

            if (refreshCookie is null || string.IsNullOrEmpty(refreshCookie.Value))
            {
                Debug.WriteLine("CookieStorage: refresh_token cookie не найден в контейнере");
                return;
            }

            var toSave = new SavedCookie
            {
                Value = refreshCookie.Value,
                Expires = refreshCookie.Expires == DateTime.MinValue
                    ? DateTime.UtcNow.AddDays(30)
                    : refreshCookie.Expires.ToUniversalTime()
            };

            await secureStorage.SaveAsync(CookieKey, toSave);
            Debug.WriteLine($"CookieStorage: Cookie сохранён, истекает {toSave.Expires:u}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CookieStorage: Ошибка сохранения: {ex.Message}");
        }
    }

    /// <summary>
    /// Удаляет сохранённый cookie — вызывать при logout.
    /// </summary>
    public async Task ClearAsync()
    {
        try
        {
            await secureStorage.RemoveAsync(CookieKey);

            var uri = new Uri($"{_apiBaseUrl}/api/auth/");
            var cookies = cookieContainer.GetCookies(uri);
            var refreshCookie = cookies[RefreshTokenCookieName];
            refreshCookie?.Expired = true;

            Debug.WriteLine("CookieStorage: Cookie удалён");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CookieStorage: Ошибка удаления: {ex.Message}");
        }
    }

    private sealed class SavedCookie
    {
        public string Value { get; set; } = string.Empty;
        public DateTime Expires { get; set; }
    }
}