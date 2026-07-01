using Core.Services.Api.Abstraction;
using Core.Shared.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace Core.Features.Shell.MainMenu.ViewModels.Chat;

public class ChatOperationsService
{
    private readonly IApiClientService _api;
    private readonly int _currentUserId;

    public ChatOperationsService(IApiClientService api, int currentUserId)
    {
        _api = api;
        _currentUserId = currentUserId;
    }

    public async Task<(bool Success, ChatDto? Data, string? Error)> CreateGroupAsync(ChatDto chatDto, List<int> memberIds,
        List<int> adminIds, Stream? avatarStream, string? avatarName)
    {
        var cr = await _api.PostAsync<ChatDto, ChatDto>(ApiEndpoints.Chats.Create, chatDto);
        if (!cr.Success || cr.Data == null)
            return (false, null, cr.Error ?? "Ошибка создания");

        var chat = cr.Data;
        await AddMembersAsync(chat.Id, memberIds);
        await SetAdminsAsync(chat.Id, adminIds);

        if (avatarStream != null && !string.IsNullOrEmpty(avatarName))
        {
            var url = await UploadAvatarAsync(chat.Id, avatarStream, avatarName);
            if (url != null) chat.Avatar = url;
        }

        return (true, chat, null);
    }

    public async Task<(bool Success, ChatDto? Data, string? Error)> UpdateGroupAsync(ChatDto chatDto, List<int> memberIds,
        List<int> adminIds, Stream? avatarStream, string? avatarName, bool avatarRemoved)
    {
        var ur = await _api.PutAsync<UpdateChatDto, ChatDto>(ApiEndpoints.Chats.ById(chatDto.Id), new UpdateChatDto
        {
            Id = chatDto.Id,
            Name = chatDto.Name,
            ChatType = ChatType.Chat,
            ShowHistoryForNewMembers = chatDto.ShowHistoryForNewMembers
        });

        if (!ur.Success || ur.Data == null)
            return (false, null, ur.Error ?? "Ошибка обновления");

        var chat = ur.Data;
        await SyncMembersAsync(chatDto.Id, memberIds, adminIds, chatDto.CreatedById);

        var (avatarOk, avatarUrl, avatarErr) =
            await UpdateAvatarAsync(chatDto.Id, avatarStream, avatarName, avatarRemoved);

        if (!avatarOk) return (false, null, avatarErr);
        if (avatarUrl != null) chat.Avatar = avatarUrl;

        return (true, chat, null);
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(int chatId)
    {
        var r = await _api.DeleteAsync(ApiEndpoints.Chats.ById(chatId));
        return r.Success ? (true, null) : (false, r.Error ?? "Ошибка удаления");
    }

    private async Task AddMembersAsync(int chatId, IEnumerable<int> memberIds)
    {
        foreach (var uid in memberIds)
            await _api.PostAsync(ApiEndpoints.Chats.Members(chatId), new UpdateChatMemberDto { UserId = uid });
    }

    private async Task SetAdminsAsync(int chatId, IEnumerable<int> adminIds)
    {
        foreach (var aid in adminIds)
            await _api.PutAsync(ApiEndpoints.Chats.MemberRole(chatId, aid, ChatRole.Admin), null!);
    }

    private async Task SyncMembersAsync(int chatId, List<int> memberIds, List<int> adminIds, int createdById)
    {
        var current = (await _api.GetAsync<List<ChatMemberDto>>(ApiEndpoints.Chats.MembersDetailed(chatId))).Data ?? [];

        var curIds = current.Select(m => m.UserId).ToHashSet();
        var curAdmins = current.Where(x => x.Role is ChatRole.Admin or ChatRole.Owner).Select(x => x.UserId).ToHashSet();

        foreach (var id in memberIds.Where(id => !curIds.Contains(id)))
            await _api.PostAsync(ApiEndpoints.Chats.Members(chatId), new UpdateChatMemberDto { UserId = id });

        foreach (var id in curIds.Where(id => !memberIds.Contains(id) && id != _currentUserId))
            await _api.DeleteAsync(ApiEndpoints.Chats.RemoveMember(chatId, id));

        foreach (var id in adminIds.Where(id => curIds.Contains(id) && !curAdmins.Contains(id)))
            await _api.PutAsync(ApiEndpoints.Chats.MemberRole(chatId, id, ChatRole.Admin), null!);

        foreach (var id in curAdmins.Where(id => id != createdById && !adminIds.Contains(id) && curIds.Contains(id)))
            await _api.PutAsync(ApiEndpoints.Chats.MemberRole(chatId, id, ChatRole.Member), null!);
    }

    private async Task<string?> UploadAvatarAsync(int chatId, Stream stream, string fileName)
    {
        stream.Position = 0;
        var r = await _api.UploadFileAsync<AvatarResponseDto>(ApiEndpoints.Chats.Avatar(chatId), stream, fileName, GetMimeType(fileName));
        return r is { Success: true, Data: not null } ? r.Data.AvatarUrl : null;
    }

    private async Task<(bool Ok, string? Url, string? Error)> UpdateAvatarAsync(int chatId, Stream? stream, string? fileName, bool removed)
    {
        if (stream != null && !string.IsNullOrEmpty(fileName))
        {
            var url = await UploadAvatarAsync(chatId, stream, fileName);
            return (true, url, null);
        }

        if (!removed) return (true, null, null);

        var del = await _api.DeleteAsync(ApiEndpoints.Chats.Avatar(chatId));
        return del.Success ? (true, string.Empty, null) : (false, null, $"Ошибка удаления аватара: {del.Error}");
    }

    private static string GetMimeType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "application/octet-stream"
    };
}