using API.Application.Common;
using API.Application.Features.Chat.Commands;
using API.Application.Features.Chat.Queries;
using API.Domain.Common;
using Shared.Contracts.Chat;
using Shared.Contracts.User;

namespace API.Application.Features.Chat;

public class ChatHandlers(
    GetUserChatsQueryHandler getUserChats,
    GetUserDialogsQueryHandler getUserDialogs,
    GetUserGroupsQueryHandler getUserGroups,
    GetChatForUserQueryHandler getChatForUser,
    GetContactChatQueryHandler getContactChat,
    GetChatMembersQueryHandler getChatMembers,
    CreateChatCommandHandler createChat,
    UpdateChatCommandHandler updateChat,
    DeleteChatCommandHandler deleteChat,
    UploadChatAvatarCommandHandler uploadAvatar,
    RemoveChatAvatarCommandHandler removeAvatar)
    : IChatHandlers
{
    public IQueryHandler<GetUserChatsQuery, Result<List<ChatDto>>> GetUserChats => getUserChats;
    public IQueryHandler<GetUserDialogsQuery, Result<List<ChatDto>>> GetUserDialogs => getUserDialogs;
    public IQueryHandler<GetUserGroupsQuery, Result<List<ChatDto>>> GetUserGroups => getUserGroups;
    public IQueryHandler<GetChatForUserQuery, Result<ChatDto>> GetChatForUser => getChatForUser;
    public IQueryHandler<GetContactChatQuery, Result<ChatDto>> GetContactChat => getContactChat;
    public IQueryHandler<GetChatMembersQuery, Result<List<UserDto>>> GetChatMembers => getChatMembers;
    public ICommandHandler<CreateChatCommand, ChatDto> CreateChat => createChat;
    public ICommandHandler<UpdateChatCommand, ChatDto> UpdateChat => updateChat;
    public ICommandHandler<DeleteChatCommand> DeleteChat => deleteChat;
    public ICommandHandler<UploadChatAvatarCommand, string> UploadAvatar => uploadAvatar;
    public ICommandHandler<RemoveChatAvatarCommand> RemoveAvatar => removeAvatar;
}