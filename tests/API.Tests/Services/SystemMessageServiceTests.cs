using API.Application.Services.Abstractions;
using API.Application.Services.Features.Chat;
using API.Domain.Common;
using API.Domain.Entities;
using API.Domain.Repositories;
using API.Infrastructure.Database;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.Message;
using Shared.Enum;
using Xunit;

namespace API.Tests.Services;

public class SystemMessageServiceTests : IDisposable
{
    private readonly MessengerDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly Mock<IChatRepository> _chatRepoMock = new();
    private readonly Mock<IHubNotifier> _hubMock = new();
    private readonly Mock<IUrlBuilder> _urlMock = new();
    private readonly SystemMessageService _service;

    public SystemMessageServiceTests()
    {
        _context = DbContextFactory.Create();
        _unitOfWork = new UnitOfWork(_context, NullLogger<UnitOfWork>.Instance);

        _service = new SystemMessageService(
            _unitOfWork,
            _chatRepoMock.Object,
            new MessageRepositoryProxy(_context),
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

        _chatRepoMock.Setup(r => r.FindByIdAsync(chat.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chat);

        await _service.CreateAsync(chat.Id, user.Id, SystemEventType.MemberAdded);

        var messages = await _context.SystemMessages.CountAsync();
        messages.Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_GroupChat_CreatesMessage()
    {
        var user = await DbContextFactory.SeedUserAsync(_context, "test", "pass");
        var chat = await DbContextFactory.SeedGroupChatAsync(_context, user.Id, "Group");

        _chatRepoMock.Setup(r => r.FindByIdAsync(chat.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chat);

        await _service.CreateAsync(chat.Id, user.Id, SystemEventType.ChatCreated);

        var messages = await _context.SystemMessages.CountAsync();
        messages.Should().Be(1);
    }

    [Fact]
    public async Task CreateCallEndedMessage_FormatsDuration()
    {
        var user = await DbContextFactory.SeedUserAsync(_context, "test", "pass");
        var chat = await DbContextFactory.SeedGroupChatAsync(_context, user.Id, "Group");

        _chatRepoMock.Setup(r => r.FindByIdAsync(chat.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chat);

        await _service.CreateCallEndedMessageAsync(chat.Id, user.Id, TimeSpan.FromMinutes(5));

        var msg = await _context.SystemMessages.FirstAsync();
        msg.Content.Should().Contain("5:00");
    }

    [Fact]
    public async Task CreateCallStartedMessage_DelegatesToCreateAsync()
    {
        var user = await DbContextFactory.SeedUserAsync(_context, "test", "pass");
        var chat = await DbContextFactory.SeedGroupChatAsync(_context, user.Id, "Group");

        _chatRepoMock.Setup(r => r.FindByIdAsync(chat.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chat);

        await _service.CreateCallStartedMessageAsync(chat.Id, user.Id);

        var msg = await _context.SystemMessages.FirstOrDefaultAsync();
        msg.Should().NotBeNull();
        msg!.SystemEventType.Should().Be(SystemEventType.CallStarted);
    }

    /// <summary>
    /// Реальная прокси-реализация IMessageRepository, которая пишет в контекст,
    /// чтобы SystemMessageService мог добавлять и читать системные сообщения.
    /// </summary>
    private sealed class MessageRepositoryProxy : IMessageRepository
    {
        private readonly MessengerDbContext _ctx;

        public MessageRepositoryProxy(MessengerDbContext ctx) => _ctx = ctx;

        // Методы, реально используемые SystemMessageService
        public void Add(Message message) => _ctx.Set<Message>().Add(message);
        public void Add(UserMessage message) => _ctx.Set<UserMessage>().Add(message);
        public void Add(SystemMessage message) => _ctx.Set<SystemMessage>().Add(message);

        public async Task<SystemMessage?> FindSystemMessageWithIncludesAsync(int messageId, CancellationToken ct = default)
            => await _ctx.SystemMessages
                .Include(m => m.Initiator)
                .FirstOrDefaultAsync(m => m.Id == messageId, ct);

        // Заглушки для неиспользуемых методов
        public Task<UserMessage?> FindUserMessageByIdAsync(int messageId, CancellationToken ct = default)
            => Task.FromResult<UserMessage?>(null);

        public Task<UserMessage?> FindUserMessageWithIncludesAsync(int messageId, CancellationToken ct = default)
            => Task.FromResult<UserMessage?>(null);

        public Task<UserMessage?> FindUserMessageForDeleteAsync(int messageId, CancellationToken ct = default)
            => Task.FromResult<UserMessage?>(null);

        public Task<UserMessage?> FindUserMessageWithIncludesNoTrackingAsync(int messageId, CancellationToken ct = default)
            => Task.FromResult<UserMessage?>(null);

        public Task<List<Message>> GetBeforeAsync(int chatId, int beforeId, int take, DateTime? cutoff = null, CancellationToken ct = default)
            => Task.FromResult(new List<Message>());

        public Task<List<Message>> GetAfterAsync(int chatId, int afterId, int take, DateTime? cutoff = null, CancellationToken ct = default)
            => Task.FromResult(new List<Message>());

        public Task<List<UserMessage>> GetUserMessagesForMixedAsync(int chatId, int? beforeId, int? afterId, DateTime? cutoff, CancellationToken ct = default)
            => Task.FromResult(new List<UserMessage>());

        public Task<List<SystemMessage>> GetSystemMessagesAsync(int chatId, int? beforeId, int? afterId, DateTime? cutoff, CancellationToken ct = default)
            => Task.FromResult(new List<SystemMessage>());

        public Task<ChatCountsDto> GetChatCountsAsync(int chatId, DateTime? cutoff)
            => Task.FromResult(new ChatCountsDto());

        public Task<List<UserMessage>> GetPinnedAsync(int chatId, DateTime? cutoff = null, CancellationToken ct = default)
            => Task.FromResult(new List<UserMessage>());

        public Task<bool> HasOlderAsync(int chatId, int beforeId, DateTime? cutoff, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> HasNewerAsync(int chatId, int afterId, DateTime? cutoff, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> ExistsInChatAsync(int messageId, int chatId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> ExistsAsync(int messageId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<(List<Message> Messages, bool HasOlder)> GetLatestAsync(int chatId, int take, DateTime? cutoff = null, CancellationToken ct = default)
            => Task.FromResult((new List<Message>(), false));

        public Task<(List<UserMessage> Items, int Total)> SearchInChatAsync(
            int chatId, string escapedQuery,
            int? senderId, DateTime? dateFrom, DateTime? dateTo,
            bool hasFiles, bool hasVoice, bool hasPoll, bool onlyText,
            bool oldestFirst, int page, int pageSize, DateTime? cutoff,
            CancellationToken ct = default)
            => Task.FromResult((new List<UserMessage>(), 0));

        public Task<(List<UserMessage> Items, int Total)> SearchGlobalAsync(
            IEnumerable<int> chatIds, string escapedQuery,
            int? senderId, DateTime? dateFrom, DateTime? dateTo,
            bool hasFiles, bool hasVoice, bool hasPoll, bool onlyText,
            bool oldestFirst, int page, int pageSize,
            Dictionary<int, DateTime> historyFilter,
            CancellationToken ct = default)
            => Task.FromResult((new List<UserMessage>(), 0));

        public Task<List<int>> GetForwardedToChatIdsAsync(int originalMessageId, CancellationToken ct = default)
            => Task.FromResult(new List<int>());

        public Task<int> SoftDeleteAsync(int messageId, DateTime editedAt, CancellationToken ct = default)
            => Task.FromResult(1);

        public Task<int> PinAsync(int messageId, int pinnedByUserId, DateTime pinnedAt, CancellationToken ct = default)
            => Task.FromResult(1);

        public Task<int> UnpinAsync(int messageId, CancellationToken ct = default)
            => Task.FromResult(1);

        public void RemoveVoiceMessage(VoiceMessage voiceMessage) { }

        // IRepository<Message>
        public Task<Message?> FindByIdAsync(int id, CancellationToken ct = default)
            => Task.FromResult<Message?>(null);

        public void Remove(Message entity) { }
    }
}