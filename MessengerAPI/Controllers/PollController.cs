using MessengerAPI.Hubs;
using MessengerAPI.Model;
using MessengerShared.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PollController(MessengerDbContext context, IHubContext<ChatHub> hubContext) : ControllerBase
{
    [HttpGet("{pollId}")]
    public async Task<ActionResult<PollDTO>> GetPoll(int pollId, [FromQuery] int userId)
    {
        var poll = await context.Polls
            .Include(p => p.PollOptions)
            .Include(p => p.PollVotes)
            .FirstOrDefaultAsync(p => p.Id == pollId);

        if (poll == null) return NotFound();

        var voteCounts = await context.PollVotes
            .Where(v => v.PollId == pollId)
            .GroupBy(v => v.OptionId)
            .Select(g => new { OptionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OptionId, x => x.Count);

        var selectedOptionIds = await context.PollVotes
            .Where(v => v.PollId == pollId && v.UserId == userId)
            .Select(v => v.OptionId)
            .ToListAsync();

        var options = poll.PollOptions.OrderBy(o => o.Position).Select(o => new PollOptionDTO
        {
            Id = o.Id,
            PollId = poll.Id,
            Text = o.OptionText,
            VotesCount = voteCounts.GetValueOrDefault(o.Id, 0),
            Votes = ! (poll.IsAnonymous ?? false)
                ? [.. context.PollVotes
                    .Where(v => v.OptionId == o.Id)
                    .Select(v => new PollVoteDTO { PollId = v.PollId, UserId = v.UserId, OptionId = v.OptionId })]
                : []
        }).ToList();

        // get chatId via related message
        var message = await context.Messages.FindAsync(poll.MessageId);
        var chatId = message?.ChatId ?? 0;

        var dto = new PollDTO
        {
            Id = poll.Id,
            MessageId = poll.MessageId,
            CreatedById = poll.CreatedById,
            ChatId = chatId,
            Question = poll.Question,
            IsAnonymous = poll.IsAnonymous ?? false,
            AllowsMultipleAnswers = poll.AllowsMultipleAnswers ?? false,
            CreatedAt = poll.CreatedAt,
            ClosesAt = poll.ClosesAt,
            Options = options,
            CanVote = !poll.PollVotes.Any(v => v.UserId == userId)
        };

        return Ok(dto);
    }

    [HttpPost]
    public async Task<ActionResult<PollDTO>> CreatePoll([FromBody] PollDTO pollDto)
    {
        var message = new Message
        {
            ChatId = pollDto.ChatId,
            SenderId = pollDto.CreatedById,
            Content = pollDto.Question,
        };

        context.Messages.Add(message);
        await context.SaveChangesAsync();

        var poll = new Poll
        {
            MessageId = message.Id,
            CreatedById = pollDto.CreatedById,
            Question = pollDto.Question,
            IsAnonymous = pollDto.IsAnonymous,
            AllowsMultipleAnswers = pollDto.AllowsMultipleAnswers,
            ClosesAt = pollDto.ClosesAt
        };

        context.Polls.Add(poll);
        await context.SaveChangesAsync();

        if (pollDto.Options != null)
        {
            int pos = 0;
            foreach (var opt in pollDto.Options)
            {
                var option = new PollOption
                {
                    PollId = poll.Id,
                    OptionText = opt.Text,
                    Position = pos++
                };
                context.PollOptions.Add(option);
            }
            await context.SaveChangesAsync();
        }

        var sender = await context.Users.FindAsync(message.SenderId);
        var createdOptions = await context.PollOptions.Where(o => o.PollId == poll.Id).OrderBy(o => o.Position).ToListAsync();

        var messageDto = new MessageDTO
        {
            Id = message.Id,
            ChatId = message.ChatId,
            SenderId = message.SenderId,
            SenderName = sender?.DisplayName ?? sender?.Username,
            Content = message.Content,
            CreatedAt = message.CreatedAt,
            Poll = new PollDTO
            {
                Id = poll.Id,
                MessageId = message.Id,
                ChatId = message.ChatId,
                CreatedById = poll.CreatedById,
                Question = poll.Question,
                IsAnonymous = poll.IsAnonymous ?? false,
                AllowsMultipleAnswers = poll.AllowsMultipleAnswers ?? false,
                CreatedAt = poll.CreatedAt,
                ClosesAt = poll.ClosesAt,
                Options = [.. createdOptions.Select(o => new PollOptionDTO
                {
                    Id = o.Id,
                    PollId = poll.Id,
                    Text = o.OptionText,
                    VotesCount = 0,
                    Votes = []
                })]
            }
        };

        try
        {
            await hubContext.Clients.Group(message.ChatId.ToString())
                .SendAsync("ReceiveMessageDTO", messageDto);
        }
        catch { }

        return Ok(messageDto);
    }

    [HttpPost("vote")]
    public async Task<IActionResult> Vote([FromBody] PollVoteDTO voteDto)
    {
        var poll = await context.Polls
            .Include(p => p.PollOptions)
            .ThenInclude(o => o.PollVotes)
            .FirstOrDefaultAsync(p => p.Id == voteDto.PollId);

        if (poll == null) return NotFound();

        // remove old votes for this user in this poll
        var oldVotes = await context.PollVotes
            .Where(v => v.PollId == voteDto.PollId && v.UserId == voteDto.UserId)
            .ToListAsync();

        context.PollVotes.RemoveRange(oldVotes);

        var vote = new PollVote
        {
            PollId = voteDto.PollId,
            OptionId = (int)voteDto.OptionId,
            UserId = voteDto.UserId,
        };

        context.PollVotes.Add(vote);
        await context.SaveChangesAsync();

        var updatedPollResult = await GetPoll(voteDto.PollId, voteDto.UserId);
        if (updatedPollResult.Result is OkObjectResult ok && ok.Value is PollDTO updated)
        {
            try
            {
                var msg = await context.Messages.FindAsync(updated.MessageId);
                if (msg != null)
                {
                    await hubContext.Clients.Group(msg.ChatId.ToString())
                        .SendAsync("ReceivePollUpdate", updated);
                }
            }
            catch { }

            return Ok(updated);
        }

        return StatusCode(500, "Failed to update poll");
    }
}
