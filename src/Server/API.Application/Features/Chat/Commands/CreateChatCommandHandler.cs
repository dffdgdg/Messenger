using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Entities;
using API.Domain.Repositories;
using Shared.Contracts.Chat;
using Shared.Enum;
using Shared.HubProtocol;

namespace API.Application.Features.Chat.Commands;

public class CreateChatCommandHandler(
    IUnitOfWork unitOfWork,
    IChatRepository chatRepository,
    IUserRepository userRepository,
    IAccessControlService accessControl,
    ISystemMessageService systemMessages,
    IHubNotifier hubNotifier,
    ICacheService cacheService,
    AppDateTime appDateTime)
    : ICommandHandler<CreateChatCommand, ChatDto>
{
    public virtual async Task<Result<ChatDto>> HandleAsync(
        CreateChatCommand command,
        CancellationToken ct = default)
    {
        var validation = await ValidateAsync(command, ct);
        if (validation.IsFailure) return validation.As<ChatDto>();

        var contactUserId = validation.Value;

        await using var tx = await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var chat = await PersistAsync(command, contactUserId, ct);
            await tx.CommitAsync(ct);

            await OnCreatedAsync(chat, command.CreatorId, contactUserId, ct);

            return Result<ChatDto>.Success(new ChatDto
            {
                Id = chat.Id,
                Name = chat.Name,
                Type = chat.Type,
                CreatedById = command.CreatorId,
                ShowHistoryForNewMembers = chat.ShowHistoryForNewMembers
            });
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // --- Validation ---

    private async Task<Result<int?>> ValidateAsync(CreateChatCommand command, CancellationToken ct)
    {
        if (command.CreatorId <= 0)
            return Result<int?>.Failure("Некорректный ID создателя");

        if (command.Type == ChatType.Contact)
            return await ValidateContactAsync(command, ct);

        if (string.IsNullOrWhiteSpace(command.Name))
            return Result<int?>.Failure("Название чата обязательно");

        return Result<int?>.Success(null);
    }

    private async Task<Result<int?>> ValidateContactAsync(
        CreateChatCommand command,
        CancellationToken ct)
    {
        // Name используется как contactUserId при создании диалога
        if (!int.TryParse(command.Name?.Trim(), out var contactId))
            return Result<int?>.Success(null);

        if (!await userRepository.ExistsAsync(contactId, ct))
            return Result<int?>.NotFound("Указанный собеседник не найден");

        var existing = await chatRepository
            .FindContactChatAsync(command.CreatorId, contactId, ct);

        if (existing is not null)
            return Result<int?>.Conflict("Диалог с этим пользователем уже существует");

        return Result<int?>.Success(contactId);
    }

    // --- Persistence ---

    private async Task<Domain.Entities.Chat> PersistAsync(
        CreateChatCommand command,
        int? contactUserId,
        CancellationToken ct)
    {
        var chat = new Domain.Entities.Chat
        {
            Name = command.Type == ChatType.Contact ? null : command.Name?.Trim(),
            Type = command.Type,
            CreatedById = command.CreatorId,
            CreatedAt = appDateTime.UtcNow,
            ShowHistoryForNewMembers = command.ShowHistoryForNewMembers
        };

        chatRepository.Add(chat);
        await unitOfWork.SaveChangesAsync(ct);

        AddMembers(chat.Id, command.CreatorId, contactUserId);
        await unitOfWork.SaveChangesAsync(ct);

        return chat;
    }

    private void AddMembers(int chatId, int creatorId, int? contactUserId)
    {
        chatRepository.AddMember(new ChatMember
        {
            ChatId = chatId,
            UserId = creatorId,
            Role = ChatRole.Owner,
            JoinedAt = appDateTime.UtcNow
        });

        if (contactUserId.HasValue && contactUserId.Value != creatorId)
        {
            chatRepository.AddMember(new ChatMember
            {
                ChatId = chatId,
                UserId = contactUserId.Value,
                Role = ChatRole.Member,
                JoinedAt = appDateTime.UtcNow
            });
        }
    }

    // --- Post-effects ---

    private async Task OnCreatedAsync(
        Domain.Entities.Chat chat,
        int creatorId,
        int? contactUserId,
        CancellationToken ct)
    {
        cacheService.InvalidateUserChats(creatorId);
        if (contactUserId.HasValue)
            cacheService.InvalidateUserChats(contactUserId.Value);

        // Системное сообщение только для групп
        if (chat.Type != ChatType.Contact)
            await systemMessages.CreateAsync(chat.Id, creatorId, SystemEventType.ChatCreated);

        // Нотификации всем участникам
        var memberIds = await chatRepository.GetMemberIdsAsync(chat.Id, ct);
        var evt = BuildUpdateEvent(chat, creatorId);

        foreach (var memberId in memberIds)
        {
            await hubNotifier.SendToUserAsync(memberId, HubMethods.Chat.ChatUpdated, evt);
            await hubNotifier.AddUserToChatGroupAsync(memberId, chat.Id);
        }
    }

    private static ChatUpdateEventDto BuildUpdateEvent(Domain.Entities.Chat chat, int creatorId) => new()
    {
        Id = chat.Id,
        Name = chat.Name,
        Type = chat.Type,
        CreatedById = creatorId,
        ShowHistoryForNewMembers = chat.ShowHistoryForNewMembers
    };
}

