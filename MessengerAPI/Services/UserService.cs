using MessengerAPI.Helpers;
using MessengerAPI.Model;
using MessengerShared.DTO;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;

namespace MessengerAPI.Services
{
    public interface IUserService
    {
        Task<List<UserDTO>> GetAllUsersAsync();
        Task<UserDTO?> GetUserAsync(int id);
        Task UpdateUserAsync(int id, UserDTO userDto);
        Task<string> UploadAvatarAsync(int id, IFormFile file, HttpRequest request);
    }
    public class UserService(MessengerDbContext context,IFileService fileService,IAccessControlService accessControl, ILogger<UserService> logger) 
        : BaseService<UserService>(context, logger), IUserService
    {
        public async Task<List<UserDTO>> GetAllUsersAsync()
        {
            try
            {
                var users = await _context.Users.Include(u => u.DepartmentNavigation).Include(u => u.UserSetting).AsNoTracking().ToListAsync();

                return [.. users.Select(u => u.ToDto())];
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
                var user = await _context.Users.Include(u => u.UserSetting).Include(u => u.DepartmentNavigation).FirstOrDefaultAsync(u => u.Id == id);

                return user?.ToDto();
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "getting user", id);
                throw;
            }
        }

        public async Task UpdateUserAsync(int id, UserDTO userDto)
        {
            try
            {
                var user = await FindEntityAsync<User>(id);
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
    }
}