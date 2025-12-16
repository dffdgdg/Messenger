// Services/UserService.cs
using MessengerAPI.Helpers;
using MessengerAPI.Model;
using MessengerShared.DTO;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services
{
    public interface IUserService
    {
        Task<List<UserDTO>> GetAllUsersAsync();
        Task<UserDTO?> GetUserAsync(int id);
        Task UpdateUserAsync(int id, UserDTO userDto);
        Task<string> UploadAvatarAsync(int id, IFormFile file, HttpRequest request);
        Task<List<int>> GetOnlineUserIdsAsync();
        Task<OnlineStatusDTO> GetOnlineStatusAsync(int userId);
        Task<List<OnlineStatusDTO>> GetOnlineStatusesAsync(List<int> userIds);
    }

    public class UserService(MessengerDbContext context,IFileService fileService,IAccessControlService accessControl,IOnlineUserService onlineUserService,ILogger<UserService> logger)
        : BaseService<UserService>(context, logger), IUserService
    {
        public async Task<List<UserDTO>> GetAllUsersAsync()
        {
            try
            {
                var users = await _context.Users
                    .Include(u => u.DepartmentNavigation)
                    .Include(u => u.UserSetting)
                    .AsNoTracking()
                    .ToListAsync();

                var onlineUserIds = onlineUserService.GetOnlineUserIds();

                return [.. users.Select(u => MapToDto(u, onlineUserIds.Contains(u.Id)))];
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "getting all users");
                throw;
            }
        }

        public async Task<UserDTO?> GetUserAsync(int id)
        {
            try
            {
                var user = await _context.Users
                    .Include(u => u.UserSetting)
                    .Include(u => u.DepartmentNavigation)
                    .FirstOrDefaultAsync(u => u.Id == id);

                if (user == null) return null;

                var isOnline = onlineUserService.IsUserOnline(id);
                return MapToDto(user, isOnline);
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "getting user", id);
                throw;
            }
        }

        public async Task<List<int>> GetOnlineUserIdsAsync()
        {
            await Task.CompletedTask;
            return [.. onlineUserService.GetOnlineUserIds()];
        }

        public async Task<OnlineStatusDTO> GetOnlineStatusAsync(int userId)
        {
            var user = await _context.Users
                .AsNoTracking()
                .Select(u => new { u.Id, u.LastOnline })
                .FirstOrDefaultAsync(u => u.Id == userId);

            return new OnlineStatusDTO
            {
                UserId = userId,
                IsOnline = onlineUserService.IsUserOnline(userId),
                LastOnline = user?.LastOnline
            };
        }

        public async Task<List<OnlineStatusDTO>> GetOnlineStatusesAsync(List<int> userIds)
        {
            var users = await _context.Users
                .Where(u => userIds.Contains(u.Id))
                .AsNoTracking()
                .Select(u => new { u.Id, u.LastOnline })
                .ToListAsync();

            var onlineIds = onlineUserService.FilterOnlineUserIds(userIds);

            return [.. users.Select(u => new OnlineStatusDTO
            {
                UserId = u.Id,
                IsOnline = onlineIds.Contains(u.Id),
                LastOnline = u.LastOnline
            })];
        }

        public async Task UpdateUserAsync(int id, UserDTO userDto)
        {
            try
            {
                var user = await _context.Users
                    .Include(u => u.UserSetting)
                    .FirstOrDefaultAsync(u => u.Id == id);

                ValidateEntityExists(user, "User", id);

                if (id != userDto.Id)
                    throw new ArgumentException("ID mismatch");

                user.DisplayName = userDto.DisplayName;
                user.Department = userDto.DepartmentId;
                user.UpdateSettings(userDto);  

                await SaveChangesAsync();
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "updating user", id);
                throw;
            }
        }

        public async Task<string> UploadAvatarAsync(int id, IFormFile file, HttpRequest request)
        {
            var user = await FindEntityAsync<User>(id);
            ValidateEntityExists(user, "User", id);

            var avatarsRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "avatars", "users");
            Directory.CreateDirectory(avatarsRoot);

            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var savePath = Path.Combine(avatarsRoot, fileName);

            using (var stream = new FileStream(savePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            user.Avatar = $"/avatars/users/{fileName}";
            await SaveChangesAsync();

            return $"{request.Scheme}://{request.Host}/avatars/users/{fileName}";
        }

        private static UserDTO MapToDto(User user, bool isOnline)
        {
            return new UserDTO
            {
                Id = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                Department = user.DepartmentNavigation?.Name,
                DepartmentId = user.DepartmentNavigation?.Id,
                Avatar = user.Avatar,
                NotificationsEnabled = user.UserSetting?.NotificationsEnabled ?? true,
                CanBeFoundInSearch = user.UserSetting?.CanBeFoundInSearch ?? true,
                Theme = user.UserSetting?.Theme ?? 0,
                IsOnline = isOnline,
                LastOnline = user.LastOnline
            };
        }
    }
}