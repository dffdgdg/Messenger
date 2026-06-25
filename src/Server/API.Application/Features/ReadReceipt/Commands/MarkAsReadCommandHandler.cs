using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.ReadReceipt;

namespace API.Application.Features.ReadReceipt.Commands;

public class MarkAsReadCommandHandler(IUnitOfWork unitOfWork, IReadReceiptRepository readReceiptRepository, AppDateTime appDateTime)
    : ICommandHandler<MarkAsReadCommand, ReadReceiptResponseDto>
{
    public virtual async Task<Result<ReadReceiptResponseDto>> HandleAsync(MarkAsReadCommand command, CancellationToken ct = default)
    {
        var (userId, request) = (command.UserId, command.Request);

        var member = await readReceiptRepository.FindMemberAsync(request.ChatId, userId, ct);
        if (member is null)
            return Result<ReadReceiptResponseDto>.Failure($"Пользователь {userId} не является участником чата {request.ChatId}");

        var targetResult = await ResolveTargetIdAsync(request, ct);
        if (targetResult.IsFailure) return targetResult.As<ReadReceiptResponseDto>();

        var targetId = targetResult.Value;

        if (targetId > 0 && (!member.LastReadMessageId.HasValue || targetId > member.LastReadMessageId.Value))
        {
            member.LastReadMessageId = targetId;
            member.LastReadAt = appDateTime.UtcNow;

            var save = await unitOfWork.SaveChangesAsync(ct);
            if (save.IsFailure) return save.As<ReadReceiptResponseDto>();
        }

        var lastReadId = member.LastReadMessageId ?? 0;
        var unreadCount = await readReceiptRepository.CountUnreadAsync(request.ChatId, userId, lastReadId, ct);

        return Result<ReadReceiptResponseDto>.Success(new ReadReceiptResponseDto
        {
            ChatId = member.ChatId,
            LastReadMessageId = member.LastReadMessageId,
            LastReadAt = member.LastReadAt,
            UnreadCount = unreadCount
        });
    }

    private async Task<Result<int>> ResolveTargetIdAsync(MarkAsReadDto request, CancellationToken ct)
    {
        if (request.MessageId.HasValue)
        {
            var exists = await readReceiptRepository
                .MessageExistsAsync(request.MessageId.Value, request.ChatId, ct);
            if (!exists)
                return Result<int>.Failure(
                    $"Сообщение {request.MessageId} не найдено в чате {request.ChatId}");
            return Result<int>.Success(request.MessageId.Value);
        }

        var lastId = await readReceiptRepository.GetLastMessageIdAsync(request.ChatId, ct);
        return Result<int>.Success(lastId);
    }
}

