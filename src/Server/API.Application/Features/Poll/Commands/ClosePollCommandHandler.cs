using API.Application.Common;
using API.Application.Features.Poll.Queries;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.Poll;
using Shared.HubProtocol;

namespace API.Application.Features.Poll.Commands;

public class ClosePollCommandHandler(
    IPollRepository pollRepository,
    IMessageRepository messageRepository,
    IAccessControlService accessControl,
    IHubNotifier hubNotifier,
    GetPollQueryHandler getPoll,
    AppDateTime appDateTime)
    : ICommandHandler<ClosePollCommand, PollDto>
{
    public virtual async Task<Result<PollDto>> HandleAsync(ClosePollCommand command, CancellationToken ct = default)
    {
        var poll = await pollRepository.FindByIdWithDetailsAsync(command.PollId, ct);
        if (poll is null)
            return Result<PollDto>.NotFound($"Опрос {command.PollId} не найден");

        if (poll.ClosesAt.HasValue && poll.ClosesAt < appDateTime.UtcNow)
            return Result<PollDto>.Failure("Опрос уже закрыт");

        var message = poll.Message;
        if (message is null)
            return Result<PollDto>.Failure("Связанное сообщение не найдено");

        var isAuthor = message.SenderId == command.UserId;
        var isAdminOrOwner = await accessControl.IsAdminAsync(command.UserId, message.ChatId)
            || await accessControl.IsOwnerAsync(command.UserId, message.ChatId);

        if (!isAuthor && !isAdminOrOwner)
            return Result<PollDto>.Forbidden("Недостаточно прав для закрытия опроса");

        await pollRepository.CloseAsync(command.PollId, appDateTime.UtcNow, ct);

        var updatedResult = await getPoll.HandleAsync(new GetPollQuery(command.PollId, command.UserId), ct);
        if (updatedResult.IsFailure) return updatedResult;

        var broadcast = new PollDto
        {
            Id = updatedResult.Value!.Id,
            MessageId = updatedResult.Value.MessageId,
            IsAnonymous = updatedResult.Value.IsAnonymous,
            AllowsMultipleAnswers = updatedResult.Value.AllowsMultipleAnswers,
            ClosesAt = updatedResult.Value.ClosesAt,
            Options = updatedResult.Value.Options,
            SelectedOptionIds = [],
            CanVote = false
        };

        var chatIds = await messageRepository.GetForwardedToChatIdsAsync(poll.MessageId, ct);
        foreach (var chatId in chatIds)
            await hubNotifier.SendToChatAsync(chatId, HubMethods.Chat.PollUpdated, broadcast);

        return updatedResult;
    }
}

