using MessengerAPI.Common;
using MessengerAPI.Services.Chat;
using MessengerShared.Dto.Chat;
using MessengerShared.Dto.User;
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
    public async Task<ActionResult<ApiResponse<List<ChatDto>>>> GetUserDialogs(int userId)
    {
        if (!IsCurrentUser(userId))
            return Forbidden<List<ChatDto>>("Доступ к чатам пользователя запрещён");

        return await ExecuteAsync(() => chatService.GetUserDialogsAsync(userId),
            "Диалоги пользователя получены успешно");
    }

    [HttpGet("user/{userId}/contact/{contactUserId}")]
    public async Task<ActionResult<ApiResponse<ChatDto>>> GetContactChat(int userId, int contactUserId)
    {
        if (!IsCurrentUser(userId))
            return Forbidden<ChatDto>();

        return await ExecuteAsync(() => chatService.GetContactChatAsync(userId, contactUserId));
    }

    [HttpGet("user/{userId}/groups")]
    public async Task<ActionResult<ApiResponse<List<ChatDto>>>> GetUserGroups(int userId)
    {
        if (!IsCurrentUser(userId))
            return Forbidden<List<ChatDto>>("Доступ к чатам пользователя запрещён");

        return await ExecuteAsync(() => chatService.GetUserGroupsAsync(userId),"Групповые чаты пользователя получены успешно");
    }

    [HttpGet("user/{userId}")]
    public async Task<ActionResult<ApiResponse<List<ChatDto>>>> GetUserChats(int userId)
    {
        if (!IsCurrentUser(userId))
            return Forbidden<List<ChatDto>>("Доступ к чатам пользователя запрещён");

        return await ExecuteAsync(() => chatService.GetUserChatsAsync(userId),"Чаты пользователя получены успешно");
    }

    [HttpGet("{chatId}")]
    public async Task<ActionResult<ApiResponse<ChatDto>>> GetChat(int chatId)
    {
        var currentUserId = GetCurrentUserId();
        return await ExecuteAsync(() => chatService.GetChatForUserAsync(chatId, currentUserId));
    }

    [HttpGet("{chatId}/members")]
    public async Task<ActionResult<ApiResponse<List<UserDto>>>> GetChatMembers(int chatId)
    {
        var currentUserId = GetCurrentUserId();
        return await ExecuteAsync(async () =>
        {
            await chatService.EnsureUserHasChatAccessAsync(currentUserId, chatId);
            var members = await chatService.GetChatMembersAsync(chatId);
            return Result<List<UserDto>>.Success(members);
        }, "Участники чата получены успешно");
    }

    [HttpPost("{chatId}/members")]
    public async Task<ActionResult<ApiResponse<ChatMemberDto>>> AddChatMember(int chatId, [FromBody] UpdateChatMemberDto dto)
    {
        var currentUserId = GetCurrentUserId();
        return await ExecuteAsync(() => chatMemberService.AddMemberAsync(chatId, dto.UserId, currentUserId),
            "Участник чата добавлен успешно");
    }

    [HttpDelete("{chatId}/members/{userId}")]
    public async Task<IActionResult> RemoveChatMember(int chatId, int userId)
    {
        var currentUserId = GetCurrentUserId();
        return await ExecuteAsync(() => chatMemberService.RemoveMemberAsync(chatId, userId, currentUserId),"Участник чата удалён успешно");
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ChatDto>>> CreateChat([FromBody] ChatDto chatDto)
    {
        chatDto.CreatedById = GetCurrentUserId();
        return await ExecuteAsync(() => chatService.CreateChatAsync(chatDto), "Чат успешно создан");
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<ChatDto>>> UpdateChat(int id, [FromBody] UpdateChatDto updateDto)
    {
        var currentUserId = GetCurrentUserId();
        return await ExecuteAsync(async () =>
        {
            if (id != updateDto.Id)
                return Result<ChatDto>.Failure("Несоответствие ID чата");

            var result = await chatService.UpdateChatAsync(id, currentUserId, updateDto);
            return Result<ChatDto>.Success(result);
        }, "Чат успешно обновлён");
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteChat(int id)
    {
        var currentUserId = GetCurrentUserId();
        return await ExecuteAsync(() => chatService.DeleteChatAsync(id, currentUserId), "Чат успешно удалён");
    }

    [HttpPost("{id}/avatar")]
    public async Task<ActionResult<ApiResponse<AvatarResponseDto>>> UploadAvatar(int id, IFormFile file)
    {
        var currentUserId = GetCurrentUserId();
        return await ExecuteAsync(async () =>
        {
            await chatService.EnsureUserIsChatAdminAsync(currentUserId, id);
            var avatarUrl = await chatService.UploadChatAvatarAsync(id, file);
            return Result<AvatarResponseDto>.Success(new AvatarResponseDto { AvatarUrl = avatarUrl });
        }, "Аватар чата загружен успешно");
    }
}