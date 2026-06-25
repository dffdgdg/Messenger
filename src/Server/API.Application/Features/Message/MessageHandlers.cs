using API.Application.Common;
using API.Application.Features.Message.Commands;
using API.Application.Features.Message.Queries;
using API.Domain.Common;
using Shared.Contracts.Message;
using Shared.Contracts.Search;

namespace API.Application.Features.Message;

public class MessageHandlers(
    CreateMessageCommandHandler create,
    UpdateMessageCommandHandler update,
    DeleteMessageCommandHandler delete,
    PinMessageCommandHandler pin,
    UnpinMessageCommandHandler unpin,
    GetLatestMessagesQueryHandler getLatest,
    GetMessagesAroundQueryHandler getAround,
    GetMessagesBeforeQueryHandler getBefore,
    GetMessagesAfterQueryHandler getAfter,
    GetPinnedMessagesQueryHandler getPinned,
    GetChatCountsQueryHandler getChatCounts,
    SearchMessagesQueryHandler search,
    GlobalSearchQueryHandler globalSearch)
    : IMessageHandlers
{
    public ICommandHandler<CreateMessageCommand, MessageDto> Create => create;
    public ICommandHandler<UpdateMessageCommand, MessageDto> Update => update;
    public ICommandHandler<DeleteMessageCommand> Delete => delete;
    public ICommandHandler<PinMessageCommand, MessageDto> Pin => pin;
    public ICommandHandler<UnpinMessageCommand, MessageDto> Unpin => unpin;
    public IQueryHandler<GetLatestMessagesQuery, Result<PagedMessagesDto>> GetLatest => getLatest;
    public IQueryHandler<GetMessagesAroundQuery, Result<PagedMessagesDto>> GetAround => getAround;
    public IQueryHandler<GetMessagesBeforeQuery, Result<PagedMessagesDto>> GetBefore => getBefore;
    public IQueryHandler<GetMessagesAfterQuery, Result<PagedMessagesDto>> GetAfter => getAfter;
    public IQueryHandler<GetPinnedMessagesQuery, Result<List<MessageDto>>> GetPinned => getPinned;
    public IQueryHandler<GetChatCountsQuery, Result<ChatCountsDto>> GetChatCounts => getChatCounts;
    public IQueryHandler<SearchMessagesQuery, Result<SearchMessagesResponseDto>> Search => search;
    public IQueryHandler<GlobalSearchQuery, Result<GlobalSearchResponseDto>> GlobalSearch => globalSearch;
}