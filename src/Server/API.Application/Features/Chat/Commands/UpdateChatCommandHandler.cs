using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.Chat;
using Shared.Enum;
using Shared.HubProtocol;

namespace API.Application.Features.Chat.Commands;

public class UpdateChatCommandHandler(
    IUnitOfWork unitOfWork,
    IChatRepository chatRepository,
    IAccessControlService accessControl,
    IHubNotifier hubNotifier,
    IUrlBuilder urlBuilder)
    : ICommandHandler<UpdateChatCommand, ChatDto>
{
    public virtual async Task<Result<ChatDto>> HandleAsync(UpdateChatCommand command, CancellationToken ct = default)
    {
        var admin = await accessControl.EnsureAdminOfAsync(command.UserId, command.ChatId);
        if (admin.IsFailure) return admin.As<ChatDto>();

        var chat = await chatRepository.FindByIdAsync(command.ChatId, ct);
        if (chat is null)
            return Result<ChatDto>.NotFound($"Чат с ID {command.ChatId} не найден");

        if (chat.Type == ChatType.Contact)
            return Result<ChatDto>.Failure("Нельзя редактировать диалог");

        var apply = await ApplyChangesAsync(chat, command);
        if (apply.IsFailure) return apply.As<ChatDto>();

        var save = await unitOfWork.SaveChangesAsync(ct);
        if (save.IsFailure) return save.As<ChatDto>();

        var evt = BuildUpdateEvent(chat);
        await hubNotifier.SendToChatAsync(command.ChatId, HubMethods.Chat.ChatUpdated, evt);

        return Result<ChatDto>.Success(new ChatDto
        {
            Id = chat.Id,
            Name = chat.Name,
            Type = chat.Type,
            CreatedById = chat.CreatedById ?? 0,
            LastMessageDate = chat.LastMessageTime,
            Avatar = urlBuilder.BuildUrl(chat.Avatar),
            ShowHistoryForNewMembers = chat.ShowHistoryForNewMembers
        });
    }

    private async Task<Result> ApplyChangesAsync(Domain.Entities.Chat chat, UpdateChatCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.Name))
            chat.Name = command.Name.Trim();

        if (command.ChatType.HasValue)
        {
            var isOwner = await accessControl.IsOwnerAsync(command.UserId, command.ChatId);
            if (!isOwner)
                return Result.Forbidden("Только владелец может изменить тип чата");

            chat.Type = command.ChatType.Value;
        }

        if (command.ShowHistoryForNewMembers.HasValue)
            chat.ShowHistoryForNewMembers = command.ShowHistoryForNewMembers.Value;

        return Result.Success();
    }

    private ChatUpdateEventDto BuildUpdateEvent(Domain.Entities.Chat chat) => new()
    {
        Id = chat.Id,
        Name = chat.Name,
        Type = chat.Type,
        CreatedById = chat.CreatedById ?? 0,
        Avatar = urlBuilder.BuildUrl(chat.Avatar),
        ShowHistoryForNewMembers = chat.ShowHistoryForNewMembers
    };
}

