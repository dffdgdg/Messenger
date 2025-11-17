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

    public class DepartmentService(MessengerDbContext context,ILogger<DepartmentService> logger) 
        : BaseService<DepartmentService>(context, logger), IDepartmentService
    {
        public async Task<List<DepartmentDTO>> GetDepartmentsAsync()
        {
            try
            {
                var departments = await _context.Departments.AsNoTracking().Select(d => new DepartmentDTO 
                {Id = d.Id, Name = d.Name, ParentDepartmentId = d.ParentDepartmentId }).ToListAsync();

                return departments;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "getting departments");
                throw;
            }
        }

        public async Task<DepartmentDTO> CreateDepartmentAsync(DepartmentDTO dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Name)) 
                    throw new ArgumentException("Name is required");

                if (dto.ParentDepartmentId.HasValue)
                {
                    var parentExists = await _context.Departments.AnyAsync(d => d.Id == dto.ParentDepartmentId.Value);

                    if (!parentExists)
                        throw new ArgumentException("Parent department does not exist");
                }

                var entity = new Department
                {
                    Name = dto.Name.Trim(),
                    ParentDepartmentId = dto.ParentDepartmentId
                };

                _context.Departments.Add(entity);
                await SaveChangesAsync();

                dto.Id = entity.Id;
                return dto;
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Validation error creating department");
                throw;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "creating department");
                throw;
            }
        }

        public async Task UpdateDepartmentAsync(int id, DepartmentDTO dto)
        {
            try
            {
                var entity = await FindEntityAsync<Department>(id);
                ValidateEntityExists(entity, "Department", id);

                if (string.IsNullOrWhiteSpace(dto.Name))
                    throw new ArgumentException("Name is required");

                if (dto.ParentDepartmentId == id)
                    throw new ArgumentException("Department cannot be its own parent");

                if (dto.ParentDepartmentId.HasValue)
                {
                    var childDepartments = await GetAllChildDepartmentsAsync(id);
                    if (childDepartments.Contains(dto.ParentDepartmentId.Value))
                        throw new ArgumentException("Cannot set a child department as parent");
                }

                entity.Name = dto.Name.Trim();
                entity.ParentDepartmentId = dto.ParentDepartmentId;

                await SaveChangesAsync();
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Validation error updating department {DepartmentId}", id);
                throw;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "updating department", id);
                throw;
            }
        }

        public async Task DeleteDepartmentAsync(int id)
        {
            try
            {
                var entity = await FindEntityAsync<Department>(id);
                ValidateEntityExists(entity, "Department", id);

                var hasChildren = await _context.Departments.AnyAsync(d => d.ParentDepartmentId == id);

                if (hasChildren)
                    throw new InvalidOperationException("Cannot delete department with child departments");

                var hasUsers = await _context.Users.AnyAsync(u => u.Department == id);

                if (hasUsers) throw new InvalidOperationException("Cannot delete department with assigned users");

                _context.Departments.Remove(entity);
                await SaveChangesAsync();
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Business rule violation deleting department {DepartmentId}", id);
                throw;
            }
            catch (Exception ex)
            {
                LogOperationError(ex, "deleting department", id);
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
                    var directChildren = departments.Where(d => d.ParentDepartmentId == parentId).Select(d => d.Id);

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
                LogOperationError(ex, "getting child departments", departmentId);
                throw;
            }
        }
    }
}