using API.Services.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace API.Tests.Services;

public class CacheServiceTests
{
    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());
    private readonly CacheService _service;

    public CacheServiceTests()
    {
        _service = new CacheService(_memoryCache, NullLogger<CacheService>.Instance);
    }

    [Fact]
    public async Task GetUserChatIds_CacheMiss_CallsFactory()
    {
        var factoryCalled = false;
        Func<Task<List<int>>> factory = () =>
        {
            factoryCalled = true;
            return Task.FromResult(new List<int> { 1, 2, 3 });
        };

        var result = await _service.GetUserChatIdsAsync(10, factory);

        factoryCalled.Should().BeTrue();
        result.Should().Equal([1, 2, 3]);
    }

    [Fact]
    public async Task GetUserChatIds_CacheHit_ReturnsCached()
    {
        // Первый вызов — промах, заполняет кэш
        await _service.GetUserChatIdsAsync(10, () => Task.FromResult(new List<int> { 1, 2 }));

        // Второй вызов — попадание
        var factoryCalled = false;
        var result = await _service.GetUserChatIdsAsync(10, () =>
        {
            factoryCalled = true;
            return Task.FromResult(new List<int>());
        });

        factoryCalled.Should().BeFalse();
        result.Should().Equal([1, 2]);
    }

    [Fact]
    public void InvalidateUserChats_RemovesFromCache()
    {
        _memoryCache.Set("user_chats_10", new List<int> { 1, 2 });
        _service.InvalidateUserChats(10);

        var cached = _memoryCache.TryGetValue("user_chats_10", out _);
        cached.Should().BeFalse();
    }

    [Fact]
    public void InvalidateMembership_RemovesBothCaches()
    {
        _memoryCache.Set("membership_10_1", new ChatMember());
        _memoryCache.Set("user_chats_10", new List<int>());

        _service.InvalidateMembership(10, 1);

        _memoryCache.TryGetValue("membership_10_1", out _).Should().BeFalse();
        _memoryCache.TryGetValue("user_chats_10", out _).Should().BeFalse();
    }

    [Fact]
    public void InvalidateChat_RemovesChatCache()
    {
        _memoryCache.Set("chat_5", new { });
        _service.InvalidateChat(5);

        _memoryCache.TryGetValue("chat_5", out _).Should().BeFalse();
    }
}