using MessengerAPI.Services.Chat;

namespace MessengerAPI.Controllers;

public sealed class ChatsController(IChatService chatService, IChatMemberService chatMemberService, ILogger<ChatsController> logger)
    : BaseController<ChatsController>(logger)
{
    [HttpGet("user/{userId}/dialogs")]
    public async Task<ActionResult<ApiResponse<List<ChatDto>>>> GetUserDialogs(int userId)
    {
        if (!IsCurrentUser(userId))
            return Forbidden<List<ChatDto>>("Доступ к чатам пользователя запрещён");

        return await ExecuteAsync(() => chatService.GetUserDialogsAsync(userId), "Диалоги пользователя получены успешно");
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

        return await ExecuteAsync(() => chatService.GetUserGroupsAsync(userId), "Групповые чаты пользователя получены успешно");
    }

    [HttpGet("user/{userId}")]
    public async Task<ActionResult<ApiResponse<List<ChatDto>>>> GetUserChats(int userId)
    {
        if (!IsCurrentUser(userId))
            return Forbidden<List<ChatDto>>("Доступ к чатам пользователя запрещён");

        return await ExecuteAsync(() => chatService.GetUserChatsAsync(userId), "Чаты пользователя получены успешно");
    }

    [HttpGet("{chatId}")]
    public async Task<ActionResult<ApiResponse<ChatDto>>> GetChat(int chatId)
        => await ExecuteAsync(() => chatService.GetChatForUserAsync(chatId, GetCurrentUserId()));

    [HttpGet("{chatId}/members")]
    public async Task<ActionResult<ApiResponse<List<UserDto>>>> GetMembers(int chatId)
        => await ExecuteAsync(() => chatService.GetChatMembersAsync(chatId, GetCurrentUserId()));

    [HttpGet("{chatId}/members/detailed")]
    public async Task<ActionResult<ApiResponse<List<ChatMemberDto>>>> GetChatMembersDetailed(int chatId)
        => await ExecuteAsync(() => chatMemberService.GetMembersAsync(chatId, GetCurrentUserId()), "Участники чата получены успешно");

    [HttpPost("{chatId}/members")]
    public async Task<ActionResult<ApiResponse<ChatMemberDto>>> AddChatMember(int chatId, [FromBody] UpdateChatMemberDto dto)
        => await ExecuteAsync(() => chatMemberService.AddMemberAsync(chatId, dto.UserId, GetCurrentUserId()), "Участник чата добавлен успешно");

    [HttpDelete("{chatId}/members/{userId}")]
    public async Task<IActionResult> RemoveChatMember(int chatId, int userId)
        => await ExecuteAsync(() => chatMemberService.RemoveMemberAsync(chatId, userId, GetCurrentUserId()), "Участник чата удалён успешно");

    [HttpPut("{chatId}/members/{userId}/role")]
    public async Task<ActionResult<ApiResponse<ChatMemberDto>>> UpdateChatMemberRole(int chatId, int userId, [FromQuery] ChatRole role)
        => await ExecuteAsync(() => chatMemberService.UpdateRoleAsync(chatId, userId, role, GetCurrentUserId()), "Роль участника чата обновлена успешно");

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ChatDto>>> CreateChat([FromBody] ChatDto chatDto)
    {
        chatDto.CreatedById = GetCurrentUserId();
        return await ExecuteAsync(() => chatService.CreateChatAsync(chatDto), "Чат успешно создан");
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<ChatDto>>> UpdateChat(int id, [FromBody] UpdateChatDto dto)
        => await ExecuteAsync(() => chatService.UpdateChatAsync(id, GetCurrentUserId(), dto), "Чат обновлён");

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteChat(int id) => await ExecuteAsync(()
        => chatService.DeleteChatAsync(id, GetCurrentUserId()), "Чат успешно удалён");

    [HttpPost("{id}/avatar")]
    public async Task<ActionResult<ApiResponse<string>>> UploadAvatar(int id, IFormFile file)
        => await ExecuteAsync(() => chatService.UploadChatAvatarAsync(id, GetCurrentUserId(), file), "Аватар обновлён");

    [HttpDelete("{id}/avatar")]
    public async Task<IActionResult> RemoveAvatar(int id)
        => await ExecuteAsync(() => chatService.RemoveChatAvatarAsync(id, GetCurrentUserId()), "Аватар удалён");
}