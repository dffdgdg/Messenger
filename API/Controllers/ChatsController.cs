using API.Application.Services.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Shared.Dto.Chat;
using Shared.Enum;

namespace API.Web.Controllers;

public sealed class ChatsController(IChatService chat, IChatMemberService member, ILogger<ChatsController> logger) : BaseController<ChatsController>(logger)
{
    [HttpGet("user/{userId}/dialogs")]
    public async Task<IActionResult> GetUserDialogs(int userId)
    {
        if (!IsCurrentUser(userId))
            return Forbidden<List<ChatDto>>("Доступ к чатам пользователя запрещён");
        return Map(await chat.GetUserDialogsAsync(userId));
    }

    [HttpGet("user/{userId}/contact/{contactUserId}")]
    public async Task<IActionResult> GetContactChat(int userId, int contactUserId)
    {
        if (!IsCurrentUser(userId))
            return Forbidden<ChatDto>();
        return Map(await chat.GetContactChatAsync(userId, contactUserId));
    }

    [HttpGet("user/{userId}/groups")]
    public async Task<IActionResult> GetUserGroups(int userId)
    {
        if (!IsCurrentUser(userId))
            return Forbidden<List<ChatDto>>("Доступ к чатам пользователя запрещён");
        return Map(await chat.GetUserGroupsAsync(userId));
    }

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserChats(int userId)
    {
        if (!IsCurrentUser(userId))
            return Forbidden<List<ChatDto>>("Доступ к чатам пользователя запрещён");
        return Map(await chat.GetUserChatsAsync(userId));
    }

    [HttpGet("{chatId}")]
    public async Task<IActionResult> GetChat(int chatId)
        => Map(await chat.GetChatForUserAsync(chatId, GetCurrentUserId()));

    [HttpGet("{chatId}/members")]
    public async Task<IActionResult> GetMembers(int chatId)
        => Map(await chat.GetChatMembersAsync(chatId, GetCurrentUserId()));

    [HttpGet("{chatId}/members/detailed")]
    public async Task<IActionResult> GetChatMembersDetailed(int chatId)
        => Map(await member.GetMembersAsync(chatId, GetCurrentUserId()));

    [HttpPost("{chatId}/members")]
    public async Task<IActionResult> AddChatMember(int chatId, [FromBody] UpdateChatMemberDto dto)
        => Map(await member.AddMemberAsync(chatId, dto.UserId, GetCurrentUserId()));

    [HttpDelete("{chatId}/members/{userId}")]
    public async Task<IActionResult> RemoveChatMember(int chatId, int userId)
        => Map(await member.RemoveMemberAsync(chatId, userId, GetCurrentUserId()));

    [HttpPut("{chatId}/members/{userId}/role")]
    public async Task<IActionResult> UpdateChatMemberRole(int chatId, int userId, [FromQuery] ChatRole role)
        => Map(await member.UpdateRoleAsync(chatId, userId, role, GetCurrentUserId()));

    [HttpPost]
    public async Task<IActionResult> CreateChat([FromBody] ChatDto chatDto)
    {
        chatDto.CreatedById = GetCurrentUserId();
        return Map(await chat.CreateChatAsync(chatDto));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateChat(int id, [FromBody] UpdateChatDto dto)
        => Map(await chat.UpdateChatAsync(id, GetCurrentUserId(), dto));

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteChat(int id)
        => Map(await chat.DeleteChatAsync(id, GetCurrentUserId()));

    [HttpPost("{id}/avatar")]
    public async Task<IActionResult> UploadAvatar(int id, IFormFile file)
        => Map(await chat.UploadChatAvatarAsync(id, GetCurrentUserId(), file));

    [HttpDelete("{id}/avatar")]
    public async Task<IActionResult> RemoveAvatar(int id)
        => Map(await chat.RemoveChatAvatarAsync(id, GetCurrentUserId()));
}