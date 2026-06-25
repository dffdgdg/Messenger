using API.Application.Common;
using API.Application.Mapping;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Entities;
using API.Domain.Repositories;
using Shared.Contracts.Message;
using Shared.HubProtocol;

namespace API.Application.Features.Poll.Commands;

public class CreatePollCommandHandler(
    IUnitOfWork unitOfWork,
    IPollRepository pollRepository,
    IMessageRepository messageRepository,
    IChatRepository chatRepository,
    IAccessControlService accessControl,
    IHubNotifier hubNotifier,
    IUrlBuilder urlBuilder,
    AppDateTime appDateTime)
    : ICommandHandler<CreatePollCommand, MessageDto>
{
    public virtual async Task<Result<MessageDto>> HandleAsync(CreatePollCommand command, CancellationToken ct = default)
    {
        var (dto, creatorId) = (command.Dto, command.CreatedByUserId);

        var access = await accessControl.EnsureMemberOfAsync(creatorId, dto.ChatId);
        if (access.IsFailure) return access.As<MessageDto>();

        if (string.IsNullOrWhiteSpace(dto.Question))
            return Result<MessageDto>.Failure("Вопрос опроса обязателен");

        if (dto.Options.Count < 2)
            return Result<MessageDto>.Failure("Опрос должен содержать минимум 2 варианта");

        await using var tx = await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var message = new UserMessage
            {
                ChatId = dto.ChatId,
                SenderId = creatorId,
                Content = dto.Question.Trim()
            };

            messageRepository.Add(message);
            await unitOfWork.SaveChangesAsync(ct);

            var poll = new Domain.Entities.Poll
            {
                MessageId = message.Id,
                IsAnonymous = dto.IsAnonymous,
                AllowsMultipleAnswers = dto.AllowsMultipleAnswers
            };

            pollRepository.Add(poll);
            await unitOfWork.SaveChangesAsync(ct);

            for (var i = 0; i < dto.Options.Count; i++)
            {
                var opt = dto.Options[i];
                pollRepository.AddOption(new PollOption
                {
                    PollId = poll.Id,
                    OptionText = opt.Text.Trim(),
                    Position = opt.Position > 0 ? opt.Position : i
                });
            }

            await unitOfWork.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            var created = await messageRepository.FindUserMessageWithIncludesAsync(message.Id, ct);
            if (created is null)
                return Result<MessageDto>.Internal("Не удалось загрузить созданное сообщение");

            var messageDto = created.ToDto(creatorId, urlBuilder);
            await hubNotifier.SendToChatAsync(dto.ChatId, HubMethods.Chat.ReceiveMessage, messageDto);

            return Result<MessageDto>.Success(messageDto);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}

