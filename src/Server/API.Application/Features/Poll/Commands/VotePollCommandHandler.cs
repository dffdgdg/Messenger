using API.Application.Common;
using API.Application.Mapping;
using API.Application.Features.Poll.Queries;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Entities;
using API.Domain.Repositories;
using Shared.Contracts.Poll;
using Shared.HubProtocol;

namespace API.Application.Features.Poll.Commands;

public class VotePollCommandHandler(
    IUnitOfWork unitOfWork,
    IPollRepository pollRepository,
    IMessageRepository messageRepository,
    IAccessControlService accessControl,
    IHubNotifier hubNotifier,
    GetPollQueryHandler getPoll,
    AppDateTime appDateTime)
    : ICommandHandler<VotePollCommand, PollDto>
{
    public virtual async Task<Result<PollDto>> HandleAsync(VotePollCommand command, CancellationToken ct = default)
    {
        var dto = command.Dto;

        var poll = await pollRepository.FindByIdWithDetailsAsync(dto.PollId, ct);
        if (poll is null)
            return Result<PollDto>.NotFound($"Опрос {dto.PollId} не найден");

        var access = await accessControl.EnsureMemberOfAsync(dto.UserId, poll.Message!.ChatId);
        if (access.IsFailure) return access.As<PollDto>();

        if (poll.ClosesAt.HasValue && poll.ClosesAt < appDateTime.UtcNow)
            return Result<PollDto>.Failure("Опрос уже закрыт");

        var optionIds = ResolveOptionIds(dto);
        var validIds = poll.PollOptions.Select(o => o.Id).ToHashSet();
        var invalidIds = optionIds.Where(id => !validIds.Contains(id)).ToList();

        if (invalidIds.Count > 0)
            return Result<PollDto>.Failure($"Невалидные варианты: {string.Join(", ", invalidIds)}");

        var oldVotes = await pollRepository.GetUserVotesAsync(dto.PollId, dto.UserId, ct);
        pollRepository.RemoveVotes(oldVotes);

        foreach (var optionId in optionIds)
        {
            pollRepository.AddVote(new PollVote
            {
                PollId = dto.PollId,
                OptionId = optionId,
                UserId = dto.UserId
            });
        }

        var save = await unitOfWork.SaveChangesAsync(ct);
        if (save.IsFailure) return save.As<PollDto>();

        var updatedResult = await getPoll.HandleAsync(
            new GetPollQuery(dto.PollId, dto.UserId), ct);
        if (updatedResult.IsFailure) return updatedResult;

        await BroadcastPollUpdateAsync(poll.MessageId, updatedResult.Value!, ct);

        return updatedResult;
    }

    private static List<int> ResolveOptionIds(PollVoteDto dto)
    {
        if (dto.OptionIds?.Count > 0) return dto.OptionIds;
        if (dto.OptionId.HasValue) return [dto.OptionId.Value];
        return [];
    }

    private async Task BroadcastPollUpdateAsync(int messageId, PollDto updated, CancellationToken ct)
    {
        var broadcast = new PollDto
        {
            Id = updated.Id,
            MessageId = updated.MessageId,
            IsAnonymous = updated.IsAnonymous,
            AllowsMultipleAnswers = updated.AllowsMultipleAnswers,
            ClosesAt = updated.ClosesAt,
            Options = updated.Options,
            SelectedOptionIds = [],
            CanVote = !updated.ClosesAt.HasValue || updated.ClosesAt > appDateTime.UtcNow
        };

        var chatIds = await messageRepository.GetForwardedToChatIdsAsync(messageId, ct);
        foreach (var chatId in chatIds)
            await hubNotifier.SendToChatAsync(chatId, HubMethods.Chat.PollUpdated, broadcast);
    }
}

