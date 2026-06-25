namespace API.Application.Features.Chat.Queries;

public sealed record GetUserChatsQuery(int UserId);
public sealed record GetUserDialogsQuery(int UserId);
public sealed record GetUserGroupsQuery(int UserId);
public sealed record GetChatForUserQuery(int ChatId, int UserId);
public sealed record GetContactChatQuery(int UserId, int ContactUserId);
public sealed record GetChatMembersQuery(int ChatId, int UserId);
