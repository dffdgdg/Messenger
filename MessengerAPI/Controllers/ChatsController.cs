using MessengerAPI.Common;
using MessengerAPI.Services.Chat;
using MessengerShared.DTO;
using MessengerShared.DTO.Chat;
using MessengerShared.DTO.User;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers;

public class ChatsController(
    IChatService chatService,
    IChatMemberService chatMemberService,
    ILogger<ChatsController> logger)
    : BaseController<ChatsController>(logger)
{
    [HttpGet("user/{userId}/dialogs")]
    public async Task<ActionResult<ApiResponse<List<ChatDTO>>>> GetUserDialogs(int userId)
    {
        if (!IsCurrentUser(userId))
            return Forbidden<List<ChatDTO>>("Доступ к чатам пользователя запрещён");

        return await ExecuteAsync(
            () => chatService.GetUserDialogsAsync(userId),
            "Диалоги пользователя получены успешно");
    }

    [HttpGet("user/{userId}/contact/{contactUserId}")]
    public async Task<ActionResult<ApiResponse<ChatDTO>>> GetContactChat(
        int userId, int contactUserId)
    {
        if (!IsCurrentUser(userId))
            return Forbidden<ChatDTO>();

        return await ExecuteAsync(
            () => chatService.GetContactChatAsync(userId, contactUserId));
    }

    [HttpGet("user/{userId}/groups")]
    public async Task<ActionResult<ApiResponse<List<ChatDTO>>>> GetUserGroups(int userId)
    {
        if (!IsCurrentUser(userId))
            return Forbidden<List<ChatDTO>>("Доступ к чатам пользователя запрещён");

        return await ExecuteAsync(
            () => chatService.GetUserGroupsAsync(userId),
            "Групповые чаты пользователя получены успешно");
    }

    [HttpGet("user/{userId}")]
    public async Task<ActionResult<ApiResponse<List<ChatDTO>>>> GetUserChats(int userId)
    {
        if (!IsCurrentUser(userId))
            return Forbidden<List<ChatDTO>>("Доступ к чатам пользователя запрещён");

        return await ExecuteAsync(
            () => chatService.GetUserChatsAsync(userId),
            "Чаты пользователя получены успешно");
    }

    [HttpGet("{chatId}")]
    public async Task<ActionResult<ApiResponse<ChatDTO>>> GetChat(int chatId)
    {
        var currentUserId = GetCurrentUserId();
        return await ExecuteAsync(
            () => chatService.GetChatForUserAsync(chatId, currentUserId));
    }

    [HttpGet("{chatId}/members")]
    public async Task<ActionResult<ApiResponse<List<UserDTO>>>> GetChatMembers(int chatId)
    {
        var currentUserId = GetCurrentUserId();
        return await ExecuteAsync(async () =>
        {
            await chatService.EnsureUserHasChatAccessAsync(currentUserId, chatId);
            var members = await chatService.GetChatMembersAsync(chatId);
            return Result<List<UserDTO>>.Success(members);
        }, "Участники чата получены успешно");
    }

    [HttpPost("{chatId}/members")]
    public async Task<ActionResult<ApiResponse<ChatMemberDTO>>> AddChatMember(
        int chatId, [FromBody] UpdateChatMemberDTO dto)
    {
        var currentUserId = GetCurrentUserId();
        return await ExecuteAsync(
            () => chatMemberService.AddMemberAsync(
                chatId, dto.UserId, currentUserId),
            "Участник чата добавлен успешно");
    }

    [HttpDelete("{chatId}/members/{userId}")]
    public async Task<IActionResult> RemoveChatMember(int chatId, int userId)
    {
        var currentUserId = GetCurrentUserId();
        return await ExecuteAsync(
            () => chatMemberService.RemoveMemberAsync(
                chatId, userId, currentUserId),
            "Участник чата удалён успешно");
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ChatDTO>>> CreateChat(
        [FromBody] ChatDTO chatDto)
    {
        chatDto.CreatedById = GetCurrentUserId();
        return await ExecuteAsync(
            () => chatService.CreateChatAsync(chatDto),
            "Чат успешно создан");
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<ChatDTO>>> UpdateChat(
        int id, [FromBody] UpdateChatDTO updateDto)
    {
        var currentUserId = GetCurrentUserId();
        return await ExecuteAsync(async () =>
        {
            if (id != updateDto.Id)
                return Result<ChatDTO>.Failure("Несоответствие ID чата");

            var result = await chatService.UpdateChatAsync(
                id, currentUserId, updateDto);
            return Result<ChatDTO>.Success(result);
        }, "Чат успешно обновлён");
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteChat(int id)
    {
        var currentUserId = GetCurrentUserId();
        return await ExecuteAsync(
            () => chatService.DeleteChatAsync(id, currentUserId),
            "Чат успешно удалён");
    }

    [HttpPost("{id}/avatar")]
    public async Task<ActionResult<ApiResponse<AvatarResponseDTO>>> UploadAvatar(
        int id, IFormFile file)
    {
        var currentUserId = GetCurrentUserId();
        return await ExecuteAsync(async () =>
        {
            await chatService.EnsureUserIsChatAdminAsync(currentUserId, id);
            var avatarUrl = await chatService.UploadChatAvatarAsync(id, file);
            return Result<AvatarResponseDTO>.Success(
                new AvatarResponseDTO { AvatarUrl = avatarUrl });
        }, "Аватар чата загружен успешно");
    }
}