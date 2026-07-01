using API.Application.Bundles;
using API.Application.Services.Abstractions;
using API.Application.Services.Features.Chat;
using API.Domain.Common;
using API.Domain.Entities;
using API.Domain.Repositories;
using API.Infrastructure.Database;
using API.Tests.Infrastructure.TestFixtures;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace API.Tests.Services;

public class ChatMemberServiceTests : IDisposable
{
    private readonly MessengerDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly Mock<IChatRepository> _chatRepoMock = new();
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly Mock<IAccessControlService> _accessMock = new();
    private readonly Mock<ISystemMessageService> _sysMsgMock = new();
    private readonly Mock<IOnlineUserService> _onlineMock = new();
    private readonly Mock<IHubNotifier> _notifierMock = new();
    private readonly ChatMemberService _service;

    public ChatMemberServiceTests()
    {
        _context = DbContextFactory.Create();
        _unitOfWork = new UnitOfWork(_context, NullLogger<UnitOfWork>.Instance);

        var cacheBundle = new CacheBundle(_accessMock.Object, _cacheMock.Object);
        var timeBundle = new TimeBundle(new AppDateTime(TimeProvider.System));
        var chatBundle = new ChatBundle(_sysMsgMock.Object, cacheBundle, timeBundle);

        _service = new ChatMemberService(
            _unitOfWork,
            _chatRepoMock.Object,
            chatBundle,
            _onlineMock.Object,
            _notifierMock.Object,
            NullLogger<ChatMemberService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task AddMember_NoAccess_ReturnsForbidden()
    {
        _accessMock.Setup(a => a.EnsureMemberOfAsync(1, 1)).ReturnsAsync(Result.Forbidden("Нет доступа"));

        var result = await _service.AddMemberAsync(1, 10, 1);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }

    [Fact]
    public async Task AddMember_AlreadyMember_ReturnsConflict()
    {
        _accessMock.Setup(a => a.IsAdminAsync(1, 1)).ReturnsAsync(false);
        _accessMock.Setup(a => a.EnsureMemberOfAsync(1, 1)).ReturnsAsync(Result.Success());
        _chatRepoMock.Setup(r => r.IsMemberAsync(1, 10, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _service.AddMemberAsync(1, 10, 1);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Conflict);
        result.Error.Should().Contain("уже является участником");
    }

    [Fact]
    public async Task RemoveMember_SelfLeave_Success()
    {
        _chatRepoMock.Setup(r => r.GetMemberAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatMember { ChatId = 1, UserId = 10, Role = ChatRole.Member, JoinedAt = DateTime.UtcNow });

        var result = await _service.RemoveMemberAsync(1, 10, 10);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RemoveMember_NotAdmin_ReturnsForbidden()
    {
        _accessMock.Setup(a => a.IsAdminAsync(1, 1)).ReturnsAsync(false);

        var result = await _service.RemoveMemberAsync(1, 10, 1);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }

    [Fact]
    public async Task RemoveMember_Owner_ReturnsForbidden()
    {
        _accessMock.Setup(a => a.IsAdminAsync(1, 1)).ReturnsAsync(true);
        _chatRepoMock.Setup(r => r.GetMemberAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatMember { ChatId = 1, UserId = 10, Role = ChatRole.Owner, JoinedAt = DateTime.UtcNow });

        var result = await _service.RemoveMemberAsync(1, 10, 1);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
        result.Error.Should().Contain("владельца");
    }

    [Fact]
    public async Task UpdateRole_NotOwner_ReturnsForbidden()
    {
        _accessMock.Setup(a => a.IsOwnerAsync(1, 1)).ReturnsAsync(false);

        var result = await _service.UpdateRoleAsync(1, 10, ChatRole.Admin, 1);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }

    [Fact]
    public async Task UpdateRole_ToOwner_ReturnsFailure()
    {
        _accessMock.Setup(a => a.IsOwnerAsync(1, 1)).ReturnsAsync(true);
        _chatRepoMock.Setup(r => r.GetMemberAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatMember { ChatId = 1, UserId = 10, Role = ChatRole.Admin, JoinedAt = DateTime.UtcNow });

        var result = await _service.UpdateRoleAsync(1, 10, ChatRole.Owner, 1);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("отдельный метод");
    }

    [Fact]
    public async Task GetMembers_NoAccess_ReturnsForbidden()
    {
        _accessMock.Setup(a => a.EnsureMemberOfAsync(10, 1)).ReturnsAsync(Result.Forbidden("Нет доступа"));
        var result = await _service.GetMembersAsync(1, 10);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }
}