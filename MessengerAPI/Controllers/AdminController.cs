using MessengerAPI.Model;
using MessengerShared.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AdminController(MessengerDbContext context) : ControllerBase
    {
        private readonly MessengerDbContext _context = context;

        // GET: api/admin (simple ping to verify controller is registered)
        [HttpGet]
        public ActionResult<string> Get() => Ok("Admin controller is up");

        // GET: api/admin/users
        [HttpGet("users")]
        public async Task<ActionResult<IEnumerable<UserDTO>>> GetUsers()
        {
            try
            {
                var userEntities = await _context.Users
                    .Include(u => u.DepartmentNavigation)
                    .Include(u => u.UserSetting)
                    .ToListAsync();

                var users = userEntities
                    .Select(u => new UserDTO
                    {
                        Id = u.Id,
                        Username = u.Username,
                        DisplayName = u.DisplayName,
                        Department = u.DepartmentNavigation?.Name,
                        DepartmentId = u.DepartmentNavigation == null ? (int?)null : u.DepartmentNavigation.Id,
                        Avatar = u.Avatar,
                        Theme = u.UserSetting != null && u.UserSetting.Theme != null ? (MessengerShared.DTO.Theme?)Enum.Parse(typeof(MessengerShared.DTO.Theme), u.UserSetting.Theme.ToString()) : null,
                        NotificationsEnabled = u.UserSetting?.NotificationsEnabled,
                        CanBeFoundInSearch = u.UserSetting?.CanBeFoundInSearch
                    })
                    .ToList();

                return Ok(users);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message, stack = ex.StackTrace });
            }
        }
    }
}
