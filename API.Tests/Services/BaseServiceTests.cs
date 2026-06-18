using API.Data;
using API.Services.Base;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace API.Tests.Services;

public class BaseServiceTests : IDisposable
{
    private readonly MessengerDbContext _context;
    private readonly TestService _service;

    public BaseServiceTests()
    {
        _context = DbContextFactory.Create();
        _service = new TestService(_context);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task SaveChangesAsync_Success_ReturnsSuccess()
    {
        var result = await _service.PublicSaveChangesAsync();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task FindEntityAsync_Found_ReturnsEntity()
    {
        var user = await DbContextFactory.SeedUserAsync(_context, "test", "pass");

        var result = await _service.PublicFindEntityAsync<User>(user.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task FindEntityAsync_NotFound_ReturnsNotFound()
    {
        var result = await _service.PublicFindEntityAsync<User>(999);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task NormalizePagination_ClampsValues()
    {
        var (page, pageSize) = TestService.PublicNormalizePagination(0, 200, 100);

        page.Should().Be(1);
        pageSize.Should().Be(100);
    }

    private class TestService : BaseService<TestService>
    {
        public TestService(MessengerDbContext context)
            : base(context, NullLogger<TestService>.Instance) { }

        public Task<Result> PublicSaveChangesAsync() => SaveChangesAsync();
        public Task<Result<T>> PublicFindEntityAsync<T>(int id) where T : class => FindEntityAsync<T>(id);
        public static (int, int) PublicNormalizePagination(int page, int pageSize, int max) => NormalizePagination(page, pageSize, max);
    }
}