using API.Data;
using API.Hubs;
using API.Services.Abstractions;
using API.Services.Features.Chat;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Enum;
using Xunit;

namespace API.Tests.Services;

public class SystemMessageServiceTests : IDisposable
{
    private readonly MessengerDbContext _context;
    private readonly Mock<IHubNotifier> _hubMock = new();
    private readonly Mock<IUrlBuilder> _urlMock = new();
    private readonly SystemMessageService _service;

    public SystemMessageServiceTests()
    {
        _context = DbContextFactory.Create();
        _service = new SystemMessageService(
            _context,
            _hubMock.Object,
            _urlMock.Object,
            new AppDateTime(TimeProvider.System),
            NullLogger<SystemMessageService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task CreateAsync_ContactChat_DoesNothing()
    {
        var user = await DbContextFactory.SeedUserAsync(_context, "test", "pass");
        var chat = await DbContextFactory.SeedContactChatAsync(_context, user.Id, user.Id + 1);

        await _service.CreateAsync(chat.Id, user.Id, SystemEventType.MemberAdded);

        var messages = await _context.SystemMessages.CountAsync();
        messages.Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_GroupChat_CreatesMessage()
    {
        var user = await DbContextFactory.SeedUserAsync(_context, "test", "pass");
        var chat = await DbContextFactory.SeedGroupChatAsync(_context, user.Id, "Group");

        await _service.CreateAsync(chat.Id, user.Id, SystemEventType.ChatCreated);

        var messages = await _context.SystemMessages.CountAsync();
        messages.Should().Be(1);
    }

    [Fact]
    public async Task CreateCallEndedMessage_FormatsDuration()
    {
        var user = await DbContextFactory.SeedUserAsync(_context, "test", "pass");
        var chat = await DbContextFactory.SeedGroupChatAsync(_context, user.Id, "Group");

        await _service.CreateCallEndedMessageAsync(chat.Id, user.Id, TimeSpan.FromMinutes(5));

        var msg = await _context.SystemMessages.FirstAsync();
        msg.Content.Should().Contain("5:00");
    }
    [Fact]
    public async Task CreateCallStartedMessage_DelegatesToCreateAsync()
    {
        var user = await DbContextFactory.SeedUserAsync(_context, "test", "pass");
        var chat = await DbContextFactory.SeedGroupChatAsync(_context, user.Id, "Group");

        await _service.CreateCallStartedMessageAsync(chat.Id, user.Id);

        var msg = await _context.SystemMessages.FirstOrDefaultAsync();
        msg.Should().NotBeNull();
        msg!.SystemEventType.Should().Be(SystemEventType.CallStarted);
    }
}