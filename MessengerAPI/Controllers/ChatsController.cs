using MessengerAPI.Services;
using MessengerShared.DTO;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers
{
    public class ChatsController(IChatService chatService, ILogger<ChatsController> logger) : BaseController<ChatsController>(logger)
    {
        [HttpGet("user/{userId}")]
        public async Task<ActionResult<ApiResponse<List<ChatDTO>>>> GetUserChats(int userId)
        {
            if (!IsCurrentUser(userId))
                return Forbidden("Access denied to user chats");

            return await ExecuteAsync(async () =>
            {
                var chats = await chatService.GetUserChatsAsync(userId, Request);
                return chats;
            }, "User chats retrieved successfully");
        }

        [HttpGet("{chatId}")]
        public async Task<ActionResult<ApiResponse<ChatDTO>>> GetChat(int chatId)
        {
            var currentUserId = GetCurrentUserId();

            return await ExecuteAsync(async () =>
            {
                await chatService.EnsureUserHasChatAccessAsync(currentUserId, chatId);
                var chat = await chatService.GetChatAsync(chatId, Request);
                return chat ?? throw new KeyNotFoundException($"Chat with ID {chatId} not found");
            });
        }

        [HttpGet("{chatId}/members")]
        public async Task<ActionResult<ApiResponse<List<UserDTO>>>> GetChatMembers(int chatId)
        {
            var currentUserId = GetCurrentUserId();

            return await ExecuteAsync(async () =>
            {
                await chatService.EnsureUserHasChatAccessAsync(currentUserId, chatId);
                var members = await chatService.GetChatMembersAsync(chatId, Request);
                return members;
            }, "Chat members retrieved successfully");
        }


        [HttpPost]
        public async Task<ActionResult<ApiResponse<ChatDTO>>> CreateChat([FromBody] ChatDTO chatDto)
        {
            var currentUserId = GetCurrentUserId();

            return await ExecuteAsync(async () =>
            {
                ValidateModel();

                if (chatDto.CreatedById != currentUserId)
                    throw new UnauthorizedAccessException("Cannot create chat for another user");

                var chat = await chatService.CreateChatAsync(chatDto); 
                return chat;
            }, "Чат успешно создан");
        }

        [HttpPost("{id}/avatar")]
        public async Task<ActionResult> UploadAvatar(int id, IFormFile file)
        {
            var currentUserId = GetCurrentUserId();

            try
            {
                if (file == null || file.Length == 0)
                    return BadRequest("No file provided");

                await chatService.EnsureUserIsChatAdminAsync(currentUserId, id);

                var avatarUrl = await chatService.UploadChatAvatarAsync(id, file, Request);
                return SuccessWithData(new { AvatarUrl = avatarUrl }, "Chat avatar uploaded successfully");
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid file or chat not found for avatar upload {ChatId}", id);
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "User {UserId} attempted to upload avatar for chat {ChatId} without permission", currentUserId, id);
                return Forbidden(ex.Message);
            }
            catch (Exception ex)
            {
                return InternalError(ex, "Error uploading chat avatar");
            }
        }
    }
}