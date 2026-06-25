using API.Application.Common;
using API.Application.Features.Chat.Commands;
using API.Application.Features.Chat.Queries;
using API.Domain.Common;
using Shared.Contracts.Chat;
using Shared.Contracts.User;

namespace API.Application.Features.Chat;

public interface IChatHandlers
{
    IQueryHandler<GetUserChatsQuery, Result<List<ChatDto>>> GetUserChats { get; }
    IQueryHandler<GetUserDialogsQuery, Result<List<ChatDto>>> GetUserDialogs { get; }
    IQueryHandler<GetUserGroupsQuery, Result<List<ChatDto>>> GetUserGroups { get; }
    IQueryHandler<GetChatForUserQuery, Result<ChatDto>> GetChatForUser { get; }
    IQueryHandler<GetContactChatQuery, Result<ChatDto>> GetContactChat { get; }
    IQueryHandler<GetChatMembersQuery, Result<List<UserDto>>> GetChatMembers { get; }
    ICommandHandler<CreateChatCommand, ChatDto> CreateChat { get; }
    ICommandHandler<UpdateChatCommand, ChatDto> UpdateChat { get; }
    ICommandHandler<DeleteChatCommand> DeleteChat { get; }
    ICommandHandler<UploadChatAvatarCommand, string> UploadAvatar { get; }
    ICommandHandler<RemoveChatAvatarCommand> RemoveAvatar { get; }
}