using MessengerAPI.Configuration;
using MessengerAPI.Helpers;
using MessengerAPI.Model;
using MessengerShared.DTO.Department;
using MessengerShared.DTO.User;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MessengerAPI.Services
{
    public interface IDepartmentService
    {
        Task<List<DepartmentDTO>> GetDepartmentsAsync();
        Task<DepartmentDTO?> GetDepartmentAsync(int id);
        Task<DepartmentDTO> CreateDepartmentAsync(DepartmentDTO dto);
        Task UpdateDepartmentAsync(int id, DepartmentDTO dto);
        Task DeleteDepartmentAsync(int id);

        Task<List<UserDTO>> GetDepartmentMembersAsync(int departmentId);
        Task AddUserToDepartmentAsync(int departmentId, int userId, int requesterId);
        Task RemoveUserFromDepartmentAsync(int departmentId, int userId, int requesterId);

        Task<bool> IsDepartmentHeadAsync(int userId, int departmentId);
        Task<bool> CanManageDepartmentAsync(int userId, int departmentId);
    }

    public class DepartmentService(MessengerDbContext context,IOptions<MessengerSettings> settings,ILogger<DepartmentService> logger)
        : BaseService<DepartmentService>(context, logger), IDepartmentService
    {
        private readonly MessengerSettings _settings = settings.Value;

        #region CRUD

        public async Task<List<DepartmentDTO>> GetDepartmentsAsync()
        {
            var departments = await _context.Departments.Include(d => d.Head).AsNoTracking().ToListAsync();

            var userCounts = await _context.Users
                .Where(u => u.Department != null)
                .GroupBy(u => u.DepartmentId)
                .Select(g => new { DepartmentId = g.Key!.Value, Count = g.Count() })
                .ToDictionaryAsync(x => x.DepartmentId, x => x.Count);

            return [.. departments.Select(d => new DepartmentDTO
            {
                Id = d.Id,
                Name = d.Name,
                ParentDepartmentId = d.ParentDepartmentId,
                Head = d.HeadId,
                HeadName = d.Head?.FormatDisplayName(),
                UserCount = userCounts.GetValueOrDefault(d.Id, 0)
            })];
        }

        public async Task<DepartmentDTO?> GetDepartmentAsync(int id)
        {
            var department = await _context.Departments.Include(d => d.Head).AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);

            if (department is null)
                return null;

            var userCount = await _context.Users.CountAsync(u => u.DepartmentId == id);

            return new DepartmentDTO
            {
                Id = department.Id,
                Name = department.Name,
                ParentDepartmentId = department.ParentDepartmentId,
                Head = department.HeadId,
                HeadName = department.Head?.FormatDisplayName(),
                UserCount = userCount
            };
        }

        public async Task<DepartmentDTO> CreateDepartmentAsync(DepartmentDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Название обязательно");

            await ValidateParentDepartmentAsync(dto.ParentDepartmentId);
            await ValidateHeadAsync(dto.Head);

            var entity = new Department
            {
                Name = dto.Name.Trim(),
                ParentDepartmentId = dto.ParentDepartmentId,
                HeadId = dto.Head
            };

            _context.Departments.Add(entity);
            await SaveChangesAsync();

            _logger.LogInformation("Отдел создан: {DepartmentId} '{Name}'", entity.Id, entity.Name);

            dto.Id = entity.Id;
            dto.UserCount = 0;

            return dto;
        }

        public async Task UpdateDepartmentAsync(int id, DepartmentDTO dto)
        {
            var entity = await _context.Departments.FindAsync(id);
            EnsureNotNull(entity, id);

            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new ArgumentException("Название обязательно");

            // Проверка циклических зависимостей
            if (dto.ParentDepartmentId == id)
                throw new ArgumentException("Отдел не может быть родителем самому себе");

            if (dto.ParentDepartmentId.HasValue)
            {
                var childIds = await GetAllChildIdsAsync(id);
                if (childIds.Contains(dto.ParentDepartmentId.Value))
                    throw new ArgumentException("Нельзя установить дочерний отдел как родительский");
            }

            await ValidateHeadAsync(dto.Head);

            entity!.Name = dto.Name.Trim();
            entity.ParentDepartmentId = dto.ParentDepartmentId;
            entity.HeadId = dto.Head;

            await SaveChangesAsync();

            _logger.LogInformation("Отдел обновлён: {DepartmentId}", id);
        }

        public async Task DeleteDepartmentAsync(int id)
        {
            var entity = await _context.Departments.FindAsync(id);
            EnsureNotNull(entity, id);

            if (await _context.Departments.AnyAsync(d => d.ParentDepartmentId == id))
                throw new InvalidOperationException("Нельзя удалить отдел с дочерними отделами");

            if (await _context.Users.AnyAsync(u => u.DepartmentId == id))
                throw new InvalidOperationException("Нельзя удалить отдел с сотрудниками");

            _context.Departments.Remove(entity!);
            await SaveChangesAsync();

            _logger.LogInformation("Отдел удалён: {DepartmentId}", id);
        }

        #endregion

        #region Members

        public async Task<List<UserDTO>> GetDepartmentMembersAsync(int departmentId)
        {
            var exists = await _context.Departments.AnyAsync(d => d.Id == departmentId);
            if (!exists) throw new KeyNotFoundException($"Отдел с ID {departmentId} не найден");

            var users = await _context.Users.Where(u => u.DepartmentId == departmentId).Include(u => u.Department).AsNoTracking().ToListAsync();

            return [.. users.Select(u => new UserDTO
            {
                Id = u.Id,
                Username = u.Username,
                DisplayName = u.FormatDisplayName(),
                Surname = u.Surname,
                Name = u.Name,
                Midname = u.Midname,
                Avatar = u.Avatar,
                DepartmentId = u.DepartmentId,
                Department = u.Department?.Name
            })];
        }

        public async Task AddUserToDepartmentAsync(int departmentId, int userId, int requesterId)
        {
            if (!await CanManageDepartmentAsync(requesterId, departmentId))
                throw new UnauthorizedAccessException("Нет прав на управление отделом");

            if (!await _context.Departments.AnyAsync(d => d.Id == departmentId))
                throw new KeyNotFoundException($"Отдел с ID {departmentId} не найден");

            var user = await _context.Users.FindAsync(userId)
                ?? throw new KeyNotFoundException($"Пользователь с ID {userId} не найден");

            if (user.DepartmentId == departmentId)
                throw new InvalidOperationException("Пользователь уже в этом отделе");

            if (user.DepartmentId.HasValue && !await IsAdminAsync(requesterId))
                throw new InvalidOperationException("Только администратор может перемещать между отделами");

            user.DepartmentId = departmentId;
            await SaveChangesAsync();

            _logger.LogInformation("Пользователь {UserId} добавлен в отдел {DepartmentId} пользователем {RequesterId}", userId, departmentId, requesterId);
        }

        public async Task RemoveUserFromDepartmentAsync(int departmentId, int userId, int requesterId)
        {
            if (!await CanManageDepartmentAsync(requesterId, departmentId))
                throw new UnauthorizedAccessException("Нет прав на управление отделом");

            var user = await _context.Users.FindAsync(userId)
                ?? throw new KeyNotFoundException($"Пользователь с ID {userId} не найден");

            if (user.DepartmentId != departmentId)
                throw new InvalidOperationException("Пользователь не в этом отделе");

            var department = await _context.Departments.FindAsync(departmentId);
            if (department?.HeadId == userId)
                throw new InvalidOperationException("Сначала назначьте другого начальника");

            user.Department = null;
            await SaveChangesAsync();

            _logger.LogInformation("Пользователь {UserId} удалён из отдела {DepartmentId} пользователем {RequesterId}", userId, departmentId, requesterId);
        }

        #endregion

        #region Permissions

        public async Task<bool> IsDepartmentHeadAsync(int userId, int departmentId)
            => await _context.Departments.AnyAsync(d => d.Id == departmentId && d.HeadId == userId);

        public async Task<bool> CanManageDepartmentAsync(int userId, int departmentId)
        {
            if (await IsAdminAsync(userId))
                return true;

            return await IsDepartmentHeadAsync(userId, departmentId);
        }

        private async Task<bool> IsAdminAsync(int userId)
            => await _context.Users.AnyAsync(u => u.Id == userId && u.DepartmentId == _settings.AdminDepartmentId);

        #endregion

        #region Validation Helpers

        private async Task ValidateParentDepartmentAsync(int? parentId)
        {
            if (!parentId.HasValue) return;

            var exists = await _context.Departments.AnyAsync(d => d.Id == parentId.Value);
            if (!exists)
                throw new ArgumentException("Родительский отдел не существует");
        }

        private async Task ValidateHeadAsync(int? headId)
        {
            if (!headId.HasValue) return;

            var exists = await _context.Users.AnyAsync(u => u.Id == headId.Value);
            if (!exists)
                throw new ArgumentException("Указанный пользователь не существует");
        }

        private async Task<HashSet<int>> GetAllChildIdsAsync(int departmentId)
        {
            var allDepartments = await _context.Departments.AsNoTracking().Select(d => new { d.Id, d.ParentDepartmentId }).ToListAsync();

            var children = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(departmentId);

            while (queue.Count > 0)
            {
                var parentId = queue.Dequeue();
                foreach (var childId in allDepartments.Where(d => d.ParentDepartmentId == parentId).Select(d => d.Id))
                {
                    if (children.Add(childId))
                    {
                        queue.Enqueue(childId);
                    }
                }
            }

            return children;
        }

        #endregion
    }
}