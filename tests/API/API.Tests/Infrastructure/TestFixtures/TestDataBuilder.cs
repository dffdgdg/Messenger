using API.Domain.Entities;
using API.Infrastructure.Database;

namespace API.Tests.Infrastructure.TestFixtures;

public class ChatBuilder(MessengerDbContext context, int creatorId)
{
    private ChatType _type = ChatType.Chat;
    private string? _name = "Test Chat";
    private readonly List<int> _memberIds = [];
    private bool _disableHistory;

    public ChatBuilder AsContact()
    {
        _type = ChatType.Contact;
        _name = null;
        return this;
    }

    public ChatBuilder Named(string name)
    {
        _name = name;
        return this;
    }

    public ChatBuilder WithMembers(params int[] userIds)
    {
        _memberIds.AddRange(userIds);
        return this;
    }

    public ChatBuilder DisableHistoryForNewMembers()
    {
        _disableHistory = true;
        return this;
    }

    public async Task<Chat> BuildAsync()
    {
        var chat = await DbContextFactory.SeedChatAsync(context, creatorId, _type, _name!, _memberIds.ToArray());

        if (_disableHistory)
        {
            chat.ShowHistoryForNewMembers = false;
            await context.SaveChangesAsync();
        }

        return chat;
    }
}

public static class DbContextBuilderExtensions
{
    public static ChatBuilder BuildChat(this MessengerDbContext context, int creatorId)
        => new(context, creatorId);
}