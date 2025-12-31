using MessengerAPI.Services;
using MessengerShared.DTO;
using MessengerShared.DTO.Chat;
using MessengerShared.DTO.User;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers
{
    public class ChatsController(IChatService chatService, ILogger<ChatsController> logger): BaseController<ChatsController>(logger)
    {
        [HttpGet("user/{userId}/dialogs")]
        public async Task<ActionResult<ApiResponse<List<ChatDTO>>>> GetUserDialogs(int userId)
        {
            if (!IsCurrentUser(userId)) return Forbidden<List<ChatDTO>>("Доступ к чатам пользователя запрещён");

            return await ExecuteAsync(async () => await chatService.GetUserDialogsAsync(userId, Request), "Диалоги пользователя получены успешно");
        }

        [HttpGet("user/{userId}/contact/{contactUserId}")]
        public async Task<ActionResult<ApiResponse<ChatDTO?>>> GetContactChat(int userId, int contactUserId)
        {
            var currentUserId = GetCurrentUserId();

            if (currentUserId != userId) return Forbidden<ChatDTO?>();

            return await ExecuteAsync(async () => await chatService.GetContactChatAsync(userId, contactUserId, Request));
        }

        [HttpGet("user/{userId}/groups")]
        public async Task<ActionResult<ApiResponse<List<ChatDTO>>>> GetUserGroups(int userId)
        {
            if (!IsCurrentUser(userId)) return Forbidden<List<ChatDTO>>("Доступ к чатам пользователя запрещён");

            return await ExecuteAsync(async () => await chatService.GetUserGroupsAsync(userId, Request), "Групповые чаты пользователя получены успешно");
        }

        [HttpGet("user/{userId}")]
        public async Task<ActionResult<ApiResponse<List<ChatDTO>>>> GetUserChats(int userId)
        {
            if (!IsCurrentUser(userId)) return Forbidden<List<ChatDTO>>("Доступ к чатам пользователя запрещён");

            return await ExecuteAsync(async () => await chatService.GetUserChatsAsync(userId, Request), "Чаты пользователя получены успешно");
        }

        [HttpGet("{chatId}")]
        public async Task<ActionResult<ApiResponse<ChatDTO>>> GetChat(int chatId)
        {
            var currentUserId = GetCurrentUserId();

            return await ExecuteAsync(async () =>
            {
                await chatService.EnsureUserHasChatAccessAsync(currentUserId, chatId);

                var chat = await chatService.GetChatAsync(chatId, currentUserId, Request);
                return chat ?? throw new KeyNotFoundException($"Чат с ID {chatId} не найден");
            });
        }

        [HttpGet("{chatId}/members")]
        public async Task<ActionResult<ApiResponse<List<UserDTO>>>> GetChatMembers(int chatId)
        {
            var currentUserId = GetCurrentUserId();

            return await ExecuteAsync(async () =>
            {
                await chatService.EnsureUserHasChatAccessAsync(currentUserId, chatId);

                return await chatService.GetChatMembersAsync(chatId, Request);
            }, "Участники чата получены успешно");
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<ChatDTO>>> CreateChat([FromBody] ChatDTO chatDto)
        {
            var currentUserId = GetCurrentUserId();

            return await ExecuteAsync(async () =>
            {
                ValidateModel();

                if (chatDto.CreatedById != currentUserId) throw new UnauthorizedAccessException("Нельзя создать чат от имени другого пользователя");

                return await chatService.CreateChatAsync(chatDto);
            }, "Чат успешно создан");
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ApiResponse<ChatDTO>>> UpdateChat(int id,[FromBody] UpdateChatDTO updateDto)
        {
            var currentUserId = GetCurrentUserId();

            return await ExecuteAsync(async () =>
            {
                ValidateModel();

                if (id != updateDto.Id) throw new ArgumentException("Несоответствие ID чата");

                return await chatService.UpdateChatAsync(id, currentUserId, updateDto, Request);
            }, "Чат успешно обновлён");
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteChat(int id)
        {
            var currentUserId = GetCurrentUserId();

            return await ExecuteAsync(async () => await chatService.DeleteChatAsync(id, currentUserId), "Чат успешно удалён");
        }

        [HttpPost("{id}/avatar")]
        public async Task<ActionResult<ApiResponse<AvatarResponseDTO>>> UploadAvatar(int id, IFormFile file)
        {
            var currentUserId = GetCurrentUserId();

            return await ExecuteAsync(async () =>
            {
                if (file == null || file.Length == 0) throw new ArgumentException("Файл не предоставлен");

                await chatService.EnsureUserIsChatAdminAsync(currentUserId, id);

                var avatarUrl = await chatService.UploadChatAvatarAsync(id, file, Request);
                return new AvatarResponseDTO { AvatarUrl = avatarUrl };
            }, "Аватар чата загружен успешно");
        }
    }
}