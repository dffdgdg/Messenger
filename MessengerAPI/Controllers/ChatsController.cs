using MessengerAPI.Model;
using MessengerShared.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;

namespace MessengerAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatsController(MessengerDbContext context) : ControllerBase
    {
        private readonly MessengerDbContext _context = context;

        // Получить чаты пользователя
        [HttpGet("user/{userId}")]
        public async Task<ActionResult<IEnumerable<ChatDTO>>> GetUserChats(int userId)
        {
            var chatIds = await _context.ChatMembers
    .Where(cm => cm.UserId == 7)
    .Select(cm => cm.ChatId)
    .ToListAsync();
            System.Diagnostics.Debug.WriteLine(string.Join(",", chatIds));

            var chats = await _context.Chats.Where(c => chatIds.Contains(c.Id)).Select(c => new ChatDTO
            {
                Id = c.Id,
                Name = c.Name,
                IsGroup = c.IsGroup,
                CreatedById = c.CreatedById ?? 0, 
                LastMessageDate = c.LastMessageTime,
                Avatar = c.Avatar != null ? $"{Request.Scheme}://{Request.Host}{c.Avatar}" : null
    })
    .ToListAsync();

            return Ok(chats);
        }

        // Получить информацию о чате по id
        [HttpGet("{chatId}")]
        public async Task<ActionResult<ChatDTO>> GetChat(int chatId)
        {
            var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Id == chatId);
            if (chat == null)
                return NotFound();

            return Ok(new ChatDTO
            {
                Id = chat.Id,
                Name = chat.Name,
                IsGroup = chat.IsGroup,
                CreatedById = (int)chat.CreatedById,
                LastMessageDate = chat.LastMessageTime,
                Avatar = chat.Avatar != null ? $"{Request.Scheme}://{Request.Host}{chat.Avatar}" : null
            });
        }

        // Получить участников чата
        [HttpGet("{chatId}/members")]
        public async Task<ActionResult<IEnumerable<UserDTO>>> GetChatMembers(int chatId)
        {
            var members = await _context.ChatMembers
                .Where(cm => cm.ChatId == chatId)
                .Include(cm => cm.User)
                .Select(cm => new UserDTO
                {
                    Id = cm.User.Id,
                    Username = cm.User.Username,
                    DisplayName = cm.User.DisplayName,
                    Avatar = cm.User.Avatar != null ? $"{Request.Scheme}://{Request.Host}{cm.User.Avatar}" : null
                })
                .ToListAsync();
            return Ok(members);
        }

        // Добавить чат
        [HttpPost]
        public async Task<ActionResult<ChatDTO>> CreateChat([FromBody] ChatDTO chatDto)
        {
            var chat = new Chat
            {
                Name = chatDto.Name,
                IsGroup = chatDto.IsGroup,
                CreatedById = chatDto.CreatedById
            };
            _context.Chats.Add(chat);
            await _context.SaveChangesAsync();

            var member = new ChatMember
            {
                ChatId = chat.Id,
                UserId = (int)chat.CreatedById,
                Role = ChatRole.owner
            };
            _context.ChatMembers.Add(member);
            await _context.SaveChangesAsync();

            chatDto.Id = chat.Id;
            return Ok(chatDto);
        }

        // Загрузить аватарку чата
        [HttpPost("{id}/avatar")]
        public async Task<IActionResult> UploadAvatar(int id, IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded");

            var chat = await _context.Chats.FindAsync(id);
            if (chat == null)
                return NotFound("Чат не найден");

            // Проверка, является ли файл изображением
            if (!file.ContentType.StartsWith("image/"))
                return BadRequest("Файл должен быть изображением");

            // Генерация имени файла
            var fileName = $"{Guid.NewGuid()}.webp";
            var relativePath = Path.Combine("avatars", "chats", fileName);
            var absolutePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", relativePath);

            // Убедиться, что каталог существует
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

            try
            {
                using var image = await Image.LoadAsync(file.OpenReadStream());
                await image.SaveAsWebpAsync(absolutePath, new WebpEncoder
                {
                    Quality = 80
                });

                if (!string.IsNullOrEmpty(chat.Avatar))
                {
                    var oldPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", chat.Avatar.TrimStart('/'));
                    if (System.IO.File.Exists(oldPath))
                        System.IO.File.Delete(oldPath);
                }

                // Обновить путь к аватарке чата в базе данных
                chat.Avatar = "/" + relativePath.Replace('\\', '/');
                await _context.SaveChangesAsync();

                return Ok(new { AvatarUrl = $"{Request.Scheme}://{Request.Host}{chat.Avatar}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Ошибка обработки изображения: {ex.Message}");
            }
        }
    }
}