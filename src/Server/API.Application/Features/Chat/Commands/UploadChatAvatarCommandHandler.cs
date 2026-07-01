using API.Application.Common;
using API.Application.Mapping;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.Chat;
using Shared.Enum;
using Shared.HubProtocol;

namespace API.Application.Features.Chat.Commands;

public class UploadChatAvatarCommandHandler(IUnitOfWork unitOfWork, IChatRepository chatRepository, IAccessControlService accessControl,
    IFileService fileService, ISystemMessageService systemMessages, IHubNotifier hubNotifier, IUrlBuilder urlBuilder)
    : ICommandHandler<UploadChatAvatarCommand, string>
{
    public virtual async Task<Result<string>> HandleAsync(UploadChatAvatarCommand command, CancellationToken ct = default)
    {
        var admin = await accessControl.EnsureAdminOfAsync(command.UserId, command.ChatId);
        if (admin.IsFailure) return admin.As<string>();

        if (command.File is null || command.File.Length == 0)
            return Result<string>.Failure("Файл не загружен");

        if (!command.File.ContentType.StartsWith("image/"))
            return Result<string>.Failure("Файл должен быть изображением");

        var chat = await chatRepository.FindByIdAsync(command.ChatId, ct);
        if (chat is null)
            return Result<string>.NotFound($"Чат с ID {command.ChatId} не найден");

        if (chat.Type == ChatType.Contact)
            return Result<string>.Failure("Нельзя установить аватар для диалога");

        var saveResult = await fileService.SaveImageAsync(command.File, "chats", chat.Avatar);
        if (saveResult.IsFailure) return saveResult.As<string>();

        chat.Avatar = saveResult.Value;

        var save = await unitOfWork.SaveChangesAsync(ct);
        if (save.IsFailure) return save.As<string>();

        await systemMessages.CreateAsync(command.ChatId, command.UserId, SystemEventType.ChatAvatarUpdated);

        await hubNotifier.SendToChatAsync(command.ChatId, HubMethods.Chat.ChatUpdated, BuildUpdateEvent(chat));

        return Result<string>.Success(AvatarUrlHelper.BuildChatAvatarUrl(urlBuilder, chat.Id, chat.Avatar)!);
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