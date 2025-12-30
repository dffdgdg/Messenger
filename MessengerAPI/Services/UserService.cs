using MessengerAPI.Helpers;
using MessengerAPI.Model;
using MessengerShared.DTO;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services
{
    public interface IUserService
    {
        Task<List<UserDTO>> GetAllUsersAsync(HttpRequest? request = null);
        Task<UserDTO?> GetUserAsync(int id, HttpRequest? request = null);
        Task UpdateUserAsync(int id, UserDTO dto);
        Task<string> UploadAvatarAsync(int id, IFormFile file, HttpRequest request);
        Task<List<int>> GetOnlineUserIdsAsync();
        Task<OnlineStatusDTO> GetOnlineStatusAsync(int userId);
        Task<List<OnlineStatusDTO>> GetOnlineStatusesAsync(List<int> userIds);
        Task ChangeUsernameAsync(int id, ChangeUsernameDTO dto);
        Task ChangePasswordAsync(int id, ChangePasswordDTO dto);
    }
    public class UserService(MessengerDbContext context,IFileService fileService,IOnlineUserService onlineService, ILogger<UserService> logger) 
        : BaseService<UserService>(context, logger), IUserService
    {
        public async Task<List<UserDTO>> GetAllUsersAsync(HttpRequest? request = null)
        {
            var users = await _context.Users.Include(u => u.Department).Include(u => u.UserSetting).AsNoTracking().ToListAsync();

            var onlineIds = onlineService.GetOnlineUserIds();

            return [.. users.Select(u => u.ToDto(request, onlineIds.Contains(u.Id)))];
        }

        public async Task<UserDTO?> GetUserAsync(int id, HttpRequest? request = null)
        {
            var user = await _context.Users.Include(u => u.UserSetting).Include(u => u.Department).AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);

            if (user == null)
                return null;

            return user.ToDto(request, onlineService.IsOnline(id));
        }

        public async Task<List<int>> GetOnlineUserIdsAsync()
        {
            await Task.CompletedTask;
            return [.. onlineService.GetOnlineUserIds()];
        }

        public async Task UpdateUserAsync(int id, UserDTO dto)
        {
            if (id != dto.Id)
                throw new ArgumentException("ID mismatch");

            var user = await _context.Users
                .Include(u => u.UserSetting)
                .FirstOrDefaultAsync(u => u.Id == id);

            EnsureNotNull(user, id);

            user!.UpdateProfile(dto);

            await SaveChangesAsync();

            _logger.LogInformation("Пользователь {UserId} обновлён", id);
        }

        public async Task<string> UploadAvatarAsync(int id, IFormFile file, HttpRequest request)
        {
            var user = await GetRequiredEntityAsync<User>(id);

            var avatarPath = await fileService.SaveImageAsync(file, "avatars/users", user.Avatar);
            user.Avatar = avatarPath;

            await SaveChangesAsync();

            _logger.LogInformation("Аватар обновлён для пользователя {UserId}", id);

            return $"{request.Scheme}://{request.Host}{avatarPath}";
        }

        public async Task<OnlineStatusDTO> GetOnlineStatusAsync(int userId)
        {
            var user = await _context.Users.AsNoTracking().Select(u => new { u.Id, u.LastOnline }).FirstOrDefaultAsync(u => u.Id == userId);

            return new OnlineStatusDTO(default, default, null)
            {
                UserId = userId,
                IsOnline = onlineService.IsOnline(userId),
                LastOnline = user?.LastOnline
            };
        }

        public async Task<List<OnlineStatusDTO>> GetOnlineStatusesAsync(List<int> userIds)
        {
            var users = await _context.Users.Where(u => userIds.Contains(u.Id)).AsNoTracking().Select(u => new { u.Id, u.LastOnline }).ToListAsync();

            var onlineIds = onlineService.FilterOnline(userIds);

            return [.. users.Select(u => new OnlineStatusDTO(default, default, null)
            {
                UserId = u.Id,
                IsOnline = onlineIds.Contains(u.Id),
                LastOnline = u.LastOnline
            })];
        }
        public async Task ChangeUsernameAsync(int id, ChangeUsernameDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.NewUsername))
                throw new ArgumentException("Username не может быть пустым");

            var username = dto.NewUsername.Trim().ToLower();

            if (!System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-z0-9_]{3,30}$"))
                throw new ArgumentException("Username должен содержать 3-30 символов (латинские буквы, цифры, подчёркивания)");

            // Проверка уникальности
            var exists = await _context.Users.AnyAsync(u => u.Username.Equals(username, StringComparison.CurrentCultureIgnoreCase) && u.Id != id);

            if (exists)
                throw new ArgumentException("Этот username уже занят");

            var user = await _context.Users.FindAsync(id);
            EnsureNotNull(user, id);

            user!.Username = username;
            await SaveChangesAsync();

            _logger.LogInformation("Username изменён для пользователя {UserId}", id);
        }

        public async Task ChangePasswordAsync(int id, ChangePasswordDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.CurrentPassword))
                throw new ArgumentException("Введите текущий пароль");

            if (string.IsNullOrWhiteSpace(dto.NewPassword))
                throw new ArgumentException("Введите новый пароль");

            if (dto.NewPassword.Length < 6)
                throw new ArgumentException("Пароль должен содержать минимум 6 символов");

            var user = await _context.Users.FindAsync(id);
            EnsureNotNull(user, id);

            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user!.PasswordHash))
                throw new ArgumentException("Неверный текущий пароль");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            await SaveChangesAsync();

            _logger.LogInformation("Пароль изменён для пользователя {UserId}", id);
        }
    }
}