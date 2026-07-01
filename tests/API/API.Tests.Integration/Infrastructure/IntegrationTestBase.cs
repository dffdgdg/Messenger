using API.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace API.Tests.Integration.Infrastructure;

/// <summary>
/// База для тестов репозиториев — работает напрямую с DbContext (InMemory EF)
/// </summary>
public abstract class IntegrationTestBase : IDisposable
{
    protected readonly MessengerDbContext Context;
    private readonly IServiceScope _scope;

    protected IntegrationTestBase()
    {
        var options = new DbContextOptionsBuilder<MessengerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        Context = new MessengerDbContext(options);
    }

    public void Dispose()
    {
        Context.Dispose();
    }
}