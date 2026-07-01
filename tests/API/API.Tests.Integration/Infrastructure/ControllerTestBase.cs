using API.Domain.Entities;
using API.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace API.Tests.Integration.Infrastructure;

/// <summary>
/// База для тестов контроллеров — работает через HttpClient + реальный pipeline
/// </summary>
public abstract class ControllerTestBase : IClassFixture<WebAppFactory>, IDisposable
{
    protected readonly WebAppFactory Factory;
    protected readonly HttpClient Client;
    protected readonly MessengerDbContext DbContext;
    private readonly IServiceScope _scope;

    protected ControllerTestBase(WebAppFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient();

        _scope = factory.Services.CreateScope();
        DbContext = _scope.ServiceProvider
            .GetRequiredService<MessengerDbContext>();
    }

    /// <summary>
    /// Авторизовать клиент — логин и сохранить токен в заголовок
    /// </summary>
    protected async Task AuthenticateAsync(string username, string password)
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Username = username,
            Password = password
        });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    /// <summary>
    /// Создать пользователя в БД и сразу авторизовать клиент под ним
    /// </summary>
    protected async Task<User> SeedAndAuthenticateAsync(
        string username = "testuser",
        string password = "Password123!")
    {
        var user = await TestDataSeeder.SeedUserAsync(DbContext, username, password);
        await AuthenticateAsync(username, password);
        return user;
    }

    public void Dispose()
    {
        _scope.Dispose();
        Client.Dispose();
    }

    // Вспомогательный record для десериализации токена
    private record TokenResponse(string AccessToken, string RefreshToken);
}