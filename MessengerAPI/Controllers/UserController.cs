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
    public class UserController(MessengerDbContext context, IWebHostEnvironment env) : ControllerBase
    {
        [HttpGet]
        public async Task<ActionResult<IEnumerable<UserDTO>>> GetAllUsers()
        {
            var users = await context.Users
                .Select(u => new UserDTO
                {
                    Id = u.Id,
                    Username = u.Username,
                    DisplayName = u.DisplayName
                })
                .ToListAsync();
            return Ok(users);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<UserDTO>> GetUser(int id)
        {
            var userEntity = await context.Users
                .Include(u => u.UserSetting)
                .Include(u => u.DepartmentNavigation)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (userEntity == null)
                return NotFound();

            MessengerShared.DTO.Theme? theme = null;
            if (userEntity.UserSetting != null && userEntity.UserSetting.Theme != null)
            {
                theme = Enum.Parse<MessengerShared.DTO.Theme>(userEntity.UserSetting.Theme.ToString());
            }

            var userDto = new UserDTO
            {
                Id = userEntity.Id,
                Username = userEntity.Username,
                DisplayName = userEntity.DisplayName,
                Department = userEntity.DepartmentNavigation?.Name,
                DepartmentId = userEntity.DepartmentNavigation == null ? (int?)null : userEntity.DepartmentNavigation.Id,
                Theme = theme,
                NotificationsEnabled = userEntity.UserSetting?.NotificationsEnabled,
                CanBeFoundInSearch = userEntity.UserSetting?.CanBeFoundInSearch,
                Avatar = userEntity.Avatar
            };

            return Ok(userDto);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(int id, UserDTO userDto)
        {
            if (id != userDto.Id)
                return BadRequest();

            var user = await context.Users
                .Include(u => u.UserSetting)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null)
                return NotFound();

            user.DisplayName = userDto.DisplayName;
            user.Department = userDto.DepartmentId;

            if (user.UserSetting == null)
            {
                user.UserSetting = new UserSetting 
                { 
                    UserId = id,
                    Theme = userDto.Theme == null ? (MessengerAPI.Model.Theme?)null : Enum.Parse<MessengerAPI.Model.Theme>(userDto.Theme.ToString()),
                    NotificationsEnabled = userDto.NotificationsEnabled ?? true,
                    CanBeFoundInSearch = userDto.CanBeFoundInSearch ?? true
                };
            }
            else 
            {
                user.UserSetting.Theme = userDto.Theme == null ? user.UserSetting.Theme ?? MessengerAPI.Model.Theme.light : Enum.Parse<MessengerAPI.Model.Theme>(userDto.Theme.ToString());
                user.UserSetting.NotificationsEnabled = userDto.NotificationsEnabled ?? user.UserSetting.NotificationsEnabled ?? true;
                user.UserSetting.CanBeFoundInSearch = userDto.CanBeFoundInSearch ?? user.UserSetting.CanBeFoundInSearch ?? true;
            }

            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await context.Users.AnyAsync(u => u.Id == id))
                    return NotFound();
                throw;
            }

            return NoContent();
        }

        [HttpPost("{id}/avatar")]
        public async Task<IActionResult> UploadAvatar(int id, IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded");

            var user = await context.Users.FindAsync(id);
            if (user == null)
                return NotFound("User not found");

            if (!file.ContentType.StartsWith("image/"))
                return BadRequest("File must be an image");

            var fileName = $"{Guid.NewGuid()}.webp";
            var relativePath = Path.Combine("avatars", "users", fileName);
            var absolutePath = Path.Combine(env.WebRootPath ?? "wwwroot", relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

            try
            {
                using var image = await Image.LoadAsync(file.OpenReadStream());
                
                var encoder = new WebpEncoder
                {
                    Quality = 80, 
                    Method = WebpEncodingMethod.Default, 
                    TransparentColorMode = WebpTransparentColorMode.Preserve
                };

                await image.SaveAsWebpAsync(absolutePath, encoder);

                if (!string.IsNullOrEmpty(user.Avatar))
                {
                    var oldPath = Path.Combine(env.WebRootPath ?? "wwwroot", user.Avatar.TrimStart('/'));
                    if (System.IO.File.Exists(oldPath))
                        System.IO.File.Delete(oldPath);
                }

                user.Avatar = "/" + relativePath.Replace('\\', '/');
                await context.SaveChangesAsync();

                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var avatarUrl = baseUrl + user.Avatar;

                return Ok(new { AvatarUrl = avatarUrl });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error processing image: {ex.Message}");
            }
        }

        [HttpGet("{id}/avatar")]
        public IActionResult GetAvatar(int id)
        {
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "UserAvatars");
            var user = context.Users.FirstOrDefault(u => u.Id == id);
            if (user == null)
                return NotFound();

            var filePath = Directory.GetFiles(uploadsFolder, $"user_{id}.*").FirstOrDefault();
            if (filePath == null)
                return NotFound();

            var ext = Path.GetExtension(filePath).ToLower();
            var contentType = ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                _ => "application/octet-stream"
            };
            return PhysicalFile(filePath, contentType);
        }
    }
}
