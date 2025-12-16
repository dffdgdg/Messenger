using MessengerAPI.Model;
using MessengerShared.DTO;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Services
{
    public interface IDepartmentService
    {
        Task<List<DepartmentDTO>> GetDepartmentsAsync();
        Task<DepartmentDTO> CreateDepartmentAsync(DepartmentDTO dto);
        Task UpdateDepartmentAsync(int id, DepartmentDTO dto);
        Task DeleteDepartmentAsync(int id);
    }

    public class DepartmentService(MessengerDbContext context, ILogger<DepartmentService> logger) : BaseService<DepartmentService>(context, logger), IDepartmentService
    {
        public async Task<List<DepartmentDTO>> GetDepartmentsAsync()
        {
            try
            {
                var departments = await _context.Departments
                    .AsNoTracking()
                    .Select(d => new DepartmentDTO
                    {
                        Id = d.Id,
                        Name = d.Name,
                        ParentDepartmentId = d.ParentDepartmentId
                    })
                    .ToListAsync();

                _logger.LogDebug("Получено {Count} отделов", departments.Count);
                return departments;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "получение отделов");
                throw;
            }
        }

        public async Task<DepartmentDTO> CreateDepartmentAsync(DepartmentDTO dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Name))
                    throw new ArgumentException("Название обязательно");

                if (dto.ParentDepartmentId.HasValue)
                {
                    var parentExists = await _context.Departments.AnyAsync(d => d.Id == dto.ParentDepartmentId.Value);

                    if (!parentExists)
                        throw new ArgumentException("Родительский отдел не существует");
                }

                var entity = new Department
                {
                    Name = dto.Name.Trim(),
                    ParentDepartmentId = dto.ParentDepartmentId
                };

                _context.Departments.Add(entity);
                await SaveChangesAsync();

                dto.Id = entity.Id;

                _logger.LogInformation("Отдел {DepartmentId} '{Name}' создан", entity.Id, entity.Name);
                return dto;
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Ошибка валидации при создании отдела");
                throw;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "создание отдела");
                throw;
            }
        }

        public async Task UpdateDepartmentAsync(int id, DepartmentDTO dto)
        {
            try
            {
                var entity = await FindEntityAsync<Department>(id);
                ValidateEntityExists(entity, "Отдел", id);

                if (string.IsNullOrWhiteSpace(dto.Name))
                    throw new ArgumentException("Название обязательно");

                if (dto.ParentDepartmentId == id)
                    throw new ArgumentException("Отдел не может быть родителем самому себе");

                if (dto.ParentDepartmentId.HasValue)
                {
                    var childDepartments = await GetAllChildDepartmentsAsync(id);
                    if (childDepartments.Contains(dto.ParentDepartmentId.Value))
                        throw new ArgumentException("Нельзя установить дочерний отдел как родительский");
                }

                entity.Name = dto.Name.Trim();
                entity.ParentDepartmentId = dto.ParentDepartmentId;

                await SaveChangesAsync();

                _logger.LogInformation("Отдел {DepartmentId} обновлён", id);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Ошибка валидации при обновлении отдела {DepartmentId}", id);
                throw;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "обновление отдела", id);
                throw;
            }
        }

        public async Task DeleteDepartmentAsync(int id)
        {
            try
            {
                var entity = await FindEntityAsync<Department>(id);
                ValidateEntityExists(entity, "Отдел", id);

                var hasChildren = await _context.Departments.AnyAsync(d => d.ParentDepartmentId == id);

                if (hasChildren)
                    throw new InvalidOperationException("Нельзя удалить отдел с дочерними отделами");

                var hasUsers = await _context.Users
                    .AnyAsync(u => u.Department == id);

                if (hasUsers)
                    throw new InvalidOperationException("Нельзя удалить отдел с назначенными пользователями");

                _context.Departments.Remove(entity);
                await SaveChangesAsync();

                _logger.LogInformation("Отдел {DepartmentId} удалён", id);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Нарушение бизнес-правила при удалении отдела {DepartmentId}", id);
                throw;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "удаление отдела", id);
                throw;
            }
        }

        private async Task<HashSet<int>> GetAllChildDepartmentsAsync(int departmentId)
        {
            try
            {
                var children = new HashSet<int>();
                var departments = await _context.Departments.AsNoTracking().ToListAsync();

                void AddChildren(int parentId)
                {
                    var directChildren = departments
                        .Where(d => d.ParentDepartmentId == parentId)
                        .Select(d => d.Id);

                    foreach (var childId in directChildren)
                    {
                        if (children.Add(childId)) AddChildren(childId);
                    }
                }

                AddChildren(departmentId);
                return children;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "получение дочерних отделов", departmentId);
                throw;
            }
        }
    }
}