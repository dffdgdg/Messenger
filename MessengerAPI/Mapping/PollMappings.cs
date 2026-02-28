using MessengerAPI.Model;
using MessengerShared.Dto.Poll;

namespace MessengerAPI.Mapping;

public static class PollMappings
{
    public static PollDto ToDto(this Poll poll, int? currentUserId = null)
    {
        var selectedOptionIds = currentUserId.HasValue
            ? poll.PollOptions?.SelectMany(o => o.PollVotes ?? [])
                .Where(v => v.UserId == currentUserId)
                .Select(v => v.OptionId).ToList() ?? []
            : [];

        return new PollDto
        {
            Id = poll.Id,
            MessageId = poll.MessageId,
            IsAnonymous = poll.IsAnonymous ?? false,
            AllowsMultipleAnswers = poll.AllowsMultipleAnswers ?? false,
            ClosesAt = poll.ClosesAt,
            Options = poll.PollOptions?.OrderBy(o => o.Position)
                .Select(o => o.ToDto(poll.IsAnonymous ?? false))
                .ToList() ?? [],
            SelectedOptionIds = selectedOptionIds,
            CanVote = selectedOptionIds.Count == 0
        };
    }

    public static PollOptionDto ToDto(this PollOption option, bool isAnonymous = false) => new()
        {
            Id = option.Id,
            PollId = option.PollId,
            Text = option.OptionText,
            Position = option.Position,
            VotesCount = option.PollVotes?.Count ?? 0,
            Votes = isAnonymous
            ? []
            : option.PollVotes?.Select(v => new PollVoteDto
            {
                PollId = v.PollId,
                UserId = v.UserId,
                OptionId = v.OptionId
            }).ToList() ?? []
        };
}