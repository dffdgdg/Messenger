using MessengerAPI.Model;
using MessengerShared.DTO;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services
{
    public interface IAdminService
    {
        Task<List<UserDTO>> GetUsersAsync();
    }

    public class AdminService(MessengerDbContext context, ILogger<AdminService> logger)
        : BaseService<AdminService>(context, logger), IAdminService
    {
        public async Task<List<UserDTO>> GetUsersAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== GetUsersAsync START ===");

                var userEntities = await _context.Users
                    .Include(u => u.DepartmentNavigation)
                    .Include(u => u.UserSetting)  
                    .AsNoTracking()
                    .ToListAsync();

                var users = userEntities.Select(u => MapUserToDto(u)).ToList();

                System.Diagnostics.Debug.WriteLine($"=== GetUsersAsync SUCCESS: {users.Count} users ===");
                return users;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"=== GetUsersAsync ERROR: {ex} ===");
                throw;
            }
        }

        private static UserDTO MapUserToDto(User user)
        {
            try
            {
                var dto = new UserDTO
                {
                    Id = user.Id,
                    Username = user.Username,
                    DisplayName = user.DisplayName,
                    Department = user.DepartmentNavigation?.Name,
                    DepartmentId = user.DepartmentNavigation?.Id,
                    Avatar = user.Avatar,
                    NotificationsEnabled = user.UserSetting?.NotificationsEnabled ?? true,
                    CanBeFoundInSearch = user.UserSetting?.CanBeFoundInSearch ?? true
                };

                if (user.UserSetting?.Theme.HasValue == true)
                {
                    dto.Theme = user.UserSetting.Theme.Value;
                }

                return dto;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in MapUserToDto for user {user.Id}: {ex}");
                return new UserDTO
                {
                    Id = user.Id,
                    Username = user.Username ?? "Error",
                    DisplayName = user.DisplayName,
                    Department = user.DepartmentNavigation?.Name,
                    DepartmentId = user.DepartmentNavigation?.Id,
                    Avatar = user.Avatar
                };
            }
        }
       
    }
}