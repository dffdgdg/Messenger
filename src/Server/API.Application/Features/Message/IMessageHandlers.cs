using API.Application.Common;
using API.Application.Features.Message.Commands;
using API.Application.Features.Message.Queries;
using API.Domain.Common;
using Shared.Contracts.Message;
using Shared.Contracts.Search;

namespace API.Application.Features.Message;

public interface IMessageHandlers
{
    ICommandHandler<CreateMessageCommand, MessageDto> Create { get; }
    ICommandHandler<UpdateMessageCommand, MessageDto> Update { get; }
    ICommandHandler<DeleteMessageCommand> Delete { get; }
    ICommandHandler<PinMessageCommand, MessageDto> Pin { get; }
    ICommandHandler<UnpinMessageCommand, MessageDto> Unpin { get; }
    IQueryHandler<GetLatestMessagesQuery, Result<PagedMessagesDto>> GetLatest { get; }
    IQueryHandler<GetMessagesAroundQuery, Result<PagedMessagesDto>> GetAround { get; }
    IQueryHandler<GetMessagesBeforeQuery, Result<PagedMessagesDto>> GetBefore { get; }
    IQueryHandler<GetMessagesAfterQuery, Result<PagedMessagesDto>> GetAfter { get; }
    IQueryHandler<GetPinnedMessagesQuery, Result<List<MessageDto>>> GetPinned { get; }
    IQueryHandler<GetChatCountsQuery, Result<ChatCountsDto>> GetChatCounts { get; }
    IQueryHandler<SearchMessagesQuery, Result<SearchMessagesResponseDto>> Search { get; }
    IQueryHandler<GlobalSearchQuery, Result<GlobalSearchResponseDto>> GlobalSearch { get; }
}