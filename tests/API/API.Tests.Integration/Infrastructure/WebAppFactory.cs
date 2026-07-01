using API.Application.Services.Abstractions;
using API.Infrastructure.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace API.Tests.Integration.Infrastructure;

public class WebAppFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<MessengerDbContext>>();
            services.RemoveAll<MessengerDbContext>();

            services.AddDbContext<MessengerDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

            services.RemoveAll<ICacheService>();
            services.AddSingleton<ICacheService, NoOpCacheService>();

            services.RemoveAll<IHubNotifier>();
            services.AddSingleton<IHubNotifier, NoOpHubNotifier>();
        });
    }

    /// <summary>
    /// Получить DbContext из DI контейнера фабрики
    /// </summary>
    public MessengerDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<MessengerDbContext>();
    }
}