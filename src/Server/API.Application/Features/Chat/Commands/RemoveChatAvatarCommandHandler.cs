using API.Application.Common;
using API.Application.Mapping;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.Chat;
using Shared.Enum;
using Shared.HubProtocol;

namespace API.Application.Features.Chat.Commands;

public class RemoveChatAvatarCommandHandler(
    IUnitOfWork unitOfWork,
    IChatRepository chatRepository,
    IAccessControlService accessControl,
    IFileService fileService,
    IHubNotifier hubNotifier,
    IUrlBuilder urlBuilder)
    : ICommandHandler<RemoveChatAvatarCommand>
{
    public virtual async Task<Result> HandleAsync(RemoveChatAvatarCommand command, CancellationToken ct = default)
    {
        var admin = await accessControl.EnsureAdminOfAsync(command.UserId, command.ChatId);
        if (admin.IsFailure) return admin;

        var chat = await chatRepository.FindByIdAsync(command.ChatId, ct);
        if (chat is null)
            return Result.NotFound($"Чат с ID {command.ChatId} не найден");

        if (chat.Type == ChatType.Contact)
            return Result.Failure("Нельзя удалить аватар у диалога");

        if (!string.IsNullOrEmpty(chat.Avatar))
            fileService.DeleteFile(chat.Avatar);

        chat.Avatar = null;

        var save = await unitOfWork.SaveChangesAsync(ct);
        if (save.IsFailure) return save;

        await hubNotifier.SendToChatAsync(command.ChatId, HubMethods.Chat.ChatUpdated, BuildUpdateEvent(chat));

        return Result.Success();
    }

    private ChatUpdateEventDto BuildUpdateEvent(Domain.Entities.Chat chat) => new()
    {
        Id = chat.Id,
        Name = chat.Name,
        Type = chat.Type,
        CreatedById = chat.CreatedById ?? 0,
        Avatar = AvatarUrlHelper.BuildChatAvatarUrl(urlBuilder, chat.Id, chat.Avatar),
        ShowHistoryForNewMembers = chat.ShowHistoryForNewMembers
    };
}

