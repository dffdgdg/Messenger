using API.Application.Features.Chat;
using API.Application.Features.Chat.Commands;
using API.Application.Features.Chat.Queries;
using API.Application.Services.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Shared.Contracts.Chat;
using Shared.Enum;

namespace API.Web.Controllers;

public sealed class ChatsController(IChatHandlers handlers, IChatMemberService memberService, ILogger<ChatsController> logger)
    : BaseController<ChatsController>(logger)
{
    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserChats(int userId)
    {
        if (!IsCurrentUser(userId))
            return Forbidden<List<ChatDto>>("Доступ к чатам пользователя запрещён");

        return Map(await handlers.GetUserChats.HandleAsync(new GetUserChatsQuery(userId)));
    }

    [HttpGet("user/{userId}/dialogs")]
    public async Task<IActionResult> GetUserDialogs(int userId)
    {
        if (!IsCurrentUser(userId))
            return Forbidden<List<ChatDto>>("Доступ к чатам пользователя запрещён");

        return Map(await handlers.GetUserDialogs.HandleAsync(new GetUserDialogsQuery(userId)));
    }

    [HttpGet("user/{userId}/groups")]
    public async Task<IActionResult> GetUserGroups(int userId)
    {
        if (!IsCurrentUser(userId))
            return Forbidden<List<ChatDto>>("Доступ к чатам пользователя запрещён");

        return Map(await handlers.GetUserGroups.HandleAsync(new GetUserGroupsQuery(userId)));
    }

    [HttpGet("user/{userId}/contact/{contactUserId}")]
    public async Task<IActionResult> GetContactChat(int userId, int contactUserId)
    {
        if (!IsCurrentUser(userId))
            return Forbidden<ChatDto>();

        return Map(await handlers.GetContactChat.HandleAsync(new GetContactChatQuery(userId, contactUserId)));
    }

    [HttpGet("{chatId}")]
    public async Task<IActionResult> GetChat(int chatId)
        => Map(await handlers.GetChatForUser.HandleAsync(new GetChatForUserQuery(chatId, GetCurrentUserId())));

    [HttpGet("{chatId}/members")]
    public async Task<IActionResult> GetMembers(int chatId)
        => Map(await handlers.GetChatMembers.HandleAsync(new GetChatMembersQuery(chatId, GetCurrentUserId())));

    [HttpGet("{chatId}/members/detailed")]
    public async Task<IActionResult> GetMembersDetailed(int chatId)
        => Map(await memberService.GetMembersAsync(chatId, GetCurrentUserId()));

    [HttpPost("{chatId}/members")]
    public async Task<IActionResult> AddMember(int chatId, [FromBody] UpdateChatMemberDto dto)
        => Map(await memberService.AddMemberAsync(chatId, dto.UserId, GetCurrentUserId()));

    [HttpDelete("{chatId}/members/{userId}")]
    public async Task<IActionResult> RemoveMember(int chatId, int userId)
        => Map(await memberService.RemoveMemberAsync(chatId, userId, GetCurrentUserId()));

    [HttpPut("{chatId}/members/{userId}/role")]
    public async Task<IActionResult> UpdateMemberRole(int chatId, int userId, [FromQuery] ChatRole role)
        => Map(await memberService.UpdateRoleAsync(chatId, userId, role, GetCurrentUserId()));

    [HttpPost]
    public async Task<IActionResult> CreateChat([FromBody] ChatDto dto)
        => Map(await handlers.CreateChat.HandleAsync(new CreateChatCommand(CreatorId: GetCurrentUserId(), Name: dto.Name, Type: dto.Type,
            ShowHistoryForNewMembers: dto.ShowHistoryForNewMembers)));

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateChat(int id, [FromBody] UpdateChatDto dto)
        => Map(await handlers.UpdateChat.HandleAsync(new UpdateChatCommand(ChatId: id, UserId: GetCurrentUserId(), Name: dto.Name,
            ChatType: dto.ChatType, ShowHistoryForNewMembers: dto.ShowHistoryForNewMembers)));

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteChat(int id)
        => Map(await handlers.DeleteChat.HandleAsync(new DeleteChatCommand(id, GetCurrentUserId())));

    [HttpPost("{id}/avatar")]
    public async Task<IActionResult> UploadAvatar(int id, IFormFile file)
        => Map(await handlers.UploadAvatar.HandleAsync(new UploadChatAvatarCommand(id, GetCurrentUserId(), file)));

    [HttpDelete("{id}/avatar")]
    public async Task<IActionResult> RemoveAvatar(int id)
        => Map(await handlers.RemoveAvatar.HandleAsync(new RemoveChatAvatarCommand(id, GetCurrentUserId())));
}