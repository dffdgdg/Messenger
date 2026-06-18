using API.Tests.Helpers;

namespace API.Tests;

public abstract class IntegrationTestBase : IDisposable
{
    protected readonly MessengerDbContext Context;

    protected IntegrationTestBase()
    {
        Context = DbContextFactory.Create();
    }

    public void Dispose() => Context.Dispose();
}