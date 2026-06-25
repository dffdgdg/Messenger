using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.HubProtocol;

namespace API.Application.Features.Chat.Commands;

public class DeleteChatCommandHandler(IUnitOfWork unitOfWork, IChatRepository chatRepository, IAccessControlService accessControl,
    IFileService fileService, IHubNotifier hubNotifier, ICacheService cacheService)
    : ICommandHandler<DeleteChatCommand>
{
    public virtual async Task<Result> HandleAsync(DeleteChatCommand command, CancellationToken ct = default)
    {
        var owner = await accessControl.EnsureOwnerOfAsync(command.UserId, command.ChatId);
        if (owner.IsFailure) return owner;

        var chat = await chatRepository.FindByIdWithMembersAsync(command.ChatId, ct);
        if (chat is null)
            return Result.NotFound($"Чат с ID {command.ChatId} не найден");

        var memberIds = chat.ChatMembers.Select(cm => cm.UserId).ToList();

        var voicePaths = await chatRepository.GetVoiceFilePathsAsync(command.ChatId, ct);
        foreach (var path in voicePaths)
            fileService.DeleteFile(path);

        chatRepository.Remove(chat);

        var save = await unitOfWork.SaveChangesAsync(ct);
        if (save.IsFailure) return save;

        foreach (var memberId in memberIds)
        {
            cacheService.InvalidateUserChats(memberId);
            cacheService.InvalidateMembership(memberId, command.ChatId);
            await hubNotifier.SendToUserAsync(memberId, HubMethods.Chat.ChatRemoved, command.ChatId);
        }

        return Result.Success();
    }
}

