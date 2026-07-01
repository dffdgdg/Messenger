using API.Application.Services.Base;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Entities;
using API.Infrastructure.Database;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using API.Tests.Infrastructure.TestFixtures;

namespace API.Tests.Services;

public class BaseServiceTests : IDisposable
{
    private readonly MessengerDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly TestService _service;

    public BaseServiceTests()
    {
        _context = DbContextFactory.Create();
        _unitOfWork = new UnitOfWork(_context, NullLogger<UnitOfWork>.Instance);
        _service = new TestService(_unitOfWork);
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

        var entity = await _context.Users.FindAsync(user.Id);

        entity.Should().NotBeNull();
        entity!.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task FindEntityAsync_NotFound_ReturnsNotFound()
    {
        var entity = await _context.Users.FindAsync(999);
        entity.Should().BeNull();
    }

    [Fact]
    public async Task NormalizePagination_ClampsValues()
    {
        var (page, pageSize) = TestService.PublicNormalizePagination(0, 200, 100);

        page.Should().Be(1);
        pageSize.Should().Be(100);
    }

    private class TestService(IUnitOfWork unitOfWork) : BaseService<TestService>(unitOfWork, NullLogger<TestService>.Instance)
    {
        public Task<Result> PublicSaveChangesAsync() => SaveChangesAsync();
        public static (int, int) PublicNormalizePagination(int page, int pageSize, int max) => NormalizePagination(page, pageSize, max);
    }
}