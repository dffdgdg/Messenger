using API.Application.Services.Abstractions;
using API.Domain.Entities;
using API.Infrastructure.Database;
using API.Infrastructure.Security;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace API.Tests.Services;

public class AccessControlServiceTests : IDisposable
{
    private readonly MessengerDbContext _context;
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock = new();
    private readonly AccessControlService _service;

    public AccessControlServiceTests()
    {
        _context = DbContextFactory.Create();
        _httpContextAccessorMock.Setup(h => h.HttpContext).Returns(new DefaultHttpContext());

        _service = new AccessControlService(
            _context,
            _cacheMock.Object,
            _httpContextAccessorMock.Object,
            NullLogger<AccessControlService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task GetUserChatIds_DelegatesToCache()
    {
        _cacheMock.Setup(c => c.GetUserChatIdsAsync(1, It.IsAny<Func<Task<List<int>>>>()))
            .ReturnsAsync([10, 20]);

        var result = await _service.GetUserChatIdsAsync(1);

        result.Should().Equal([10, 20]);
    }

    [Fact]
    public async Task IsMember_True_WhenMemberExists()
    {
        _cacheMock.Setup(c => c.GetMembershipAsync(1, 10, It.IsAny<Func<Task<ChatMember?>>>()))
            .ReturnsAsync(new ChatMember { ChatId = 10, UserId = 1, Role = ChatRole.Member });

        var result = await _service.IsMemberAsync(1, 10);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsMember_False_WhenMemberNotFound()
    {
        _cacheMock.Setup(c => c.GetMembershipAsync(1, 10, It.IsAny<Func<Task<ChatMember?>>>()))
            .ReturnsAsync((ChatMember?)null);

        var result = await _service.IsMemberAsync(1, 10);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsOwner_True_WhenRoleIsOwner()
    {
        _cacheMock.Setup(c => c.GetMembershipAsync(1, 10, It.IsAny<Func<Task<ChatMember?>>>()))
            .ReturnsAsync(new ChatMember { ChatId = 10, UserId = 1, Role = ChatRole.Owner });

        var result = await _service.IsOwnerAsync(1, 10);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsAdmin_True_WhenRoleIsAdmin()
    {
        _cacheMock.Setup(c => c.GetMembershipAsync(1, 10, It.IsAny<Func<Task<ChatMember?>>>()))
            .ReturnsAsync(new ChatMember { ChatId = 10, UserId = 1, Role = ChatRole.Admin });

        var result = await _service.IsAdminAsync(1, 10);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetRole_ReturnsRole()
    {
        _cacheMock.Setup(c => c.GetMembershipAsync(1, 10, It.IsAny<Func<Task<ChatMember?>>>()))
            .ReturnsAsync(new ChatMember { ChatId = 10, UserId = 1, Role = ChatRole.Member });

        var result = await _service.GetRoleAsync(1, 10);

        result.Should().Be(ChatRole.Member);
    }

    [Fact]
    public async Task GetChatMemberIds_ReturnsFromDb()
    {
        var user = await DbContextFactory.SeedUserAsync(_context, "test", "pass");
        var chat = await DbContextFactory.SeedGroupChatAsync(_context, user.Id, "Group");
        _context.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = 999, Role = ChatRole.Member, JoinedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        var result = await _service.GetChatMemberIdsAsync(chat.Id);

        result.Should().Contain(user.Id);
        result.Should().Contain(999);
    }

    [Fact]
    public void InvalidateSystemAdminCache_DoesNotThrow()
    {
        _service.InvalidateSystemAdminCache();
    }
}