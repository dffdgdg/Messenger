using MessengerAPI.Common;
using MessengerAPI.Configuration;
using MessengerAPI.Mapping;
using MessengerAPI.Model;
using MessengerAPI.Services.Base;
using MessengerShared.Dto.Department;
using MessengerShared.Dto.User;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MessengerAPI.Services.Department;

public interface IDepartmentService
{
    Task<Result<List<DepartmentDto>>> GetDepartmentsAsync(CancellationToken ct = default);
    Task<Result<DepartmentDto>> GetDepartmentAsync(int id, CancellationToken ct = default);
    Task<Result<DepartmentDto>> CreateDepartmentAsync(DepartmentDto dto, CancellationToken ct = default);
    Task<Result> UpdateDepartmentAsync(int id, DepartmentDto dto, CancellationToken ct = default);
    Task<Result> DeleteDepartmentAsync(int id, CancellationToken ct = default);
    Task<Result<List<UserDto>>> GetDepartmentMembersAsync(int departmentId, CancellationToken ct = default);
    Task<Result> AddUserToDepartmentAsync(int departmentId, int userId, int requesterId, CancellationToken ct = default);
    Task<Result> RemoveUserFromDepartmentAsync(int departmentId, int userId, int requesterId, CancellationToken ct = default);
    Task<Result<bool>> CanManageDepartmentAsync(int userId, int departmentId, CancellationToken ct = default);
}

public class DepartmentService(MessengerDbContext context, IOptions<MessengerSettings> settings, ILogger<DepartmentService> logger)
: BaseService<DepartmentService>(context, logger), IDepartmentService
{
    private readonly MessengerSettings _settings = settings.Value;

    public async Task<Result<List<DepartmentDto>>> GetDepartmentsAsync(CancellationToken ct = default)
    {
        var departments = await _context.Departments
            .Include(d => d.Head).AsNoTracking()
            .ToListAsync(ct);

        var userCounts = await _context.Users
            .Where(u => u.Department != null)
            .GroupBy(u => u.DepartmentId)
            .Select(g => new { DepartmentId = g.Key!.Value, Count = g.Count() })
            .ToDictionaryAsync(x => x.DepartmentId, x => x.Count, ct);

        var result = departments.ConvertAll(d => new DepartmentDto
        {
            Id = d.Id,
            Name = d.Name,
            ParentDepartmentId = d.ParentDepartmentId,
            Head = d.HeadId,
            HeadName = d.Head?.FormatDisplayName(),
            UserCount = userCounts.GetValueOrDefault(d.Id, 0)
        });

        return Result<List<DepartmentDto>>.Success(result);
    }

    public async Task<Result<DepartmentDto>> GetDepartmentAsync(int id, CancellationToken ct = default)
    {
        var department = await _context.Departments
            .Include(d => d.Head).AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, ct);

        if (department is null)
            return Result<DepartmentDto>.Failure($"Отдел с ID {id} не найден");

        var userCount = await _context.Users.CountAsync(u => u.DepartmentId == id, ct);

        return Result<DepartmentDto>.Success(new DepartmentDto
        {
            Id = department.Id,
            Name = department.Name,
            ParentDepartmentId = department.ParentDepartmentId,
            Head = department.HeadId,
            HeadName = department.Head?.FormatDisplayName(),
            UserCount = userCount
        });
    }

    public async Task<Result<DepartmentDto>> CreateDepartmentAsync(DepartmentDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return Result<DepartmentDto>.Failure("Название обязательно");

        if (dto.ParentDepartmentId.HasValue)
        {
            var parentExists = await _context.Departments.AnyAsync(d => d.Id == dto.ParentDepartmentId.Value, ct);
            if (!parentExists)
                return Result<DepartmentDto>.Failure("Родительский отдел не существует");
        }

        if (dto.Head.HasValue)
        {
            var headExists = await _context.Users.AnyAsync(u => u.Id == dto.Head.Value, ct);
            if (!headExists)
                return Result<DepartmentDto>.Failure("Указанный пользователь не существует");
        }

        var entity = new Model.Department
        {
            Name = dto.Name.Trim(),
            ParentDepartmentId = dto.ParentDepartmentId,
            HeadId = dto.Head
        };

        _context.Departments.Add(entity);
        await SaveChangesAsync(ct);

        _logger.LogInformation("Отдел создан: {DepartmentId} '{Name}'",entity.Id, entity.Name);

        dto.Id = entity.Id;
        dto.UserCount = 0;

        return Result<DepartmentDto>.Success(dto);
    }

    public async Task<Result> UpdateDepartmentAsync(int id, DepartmentDto dto, CancellationToken ct = default)
    {
        var entity = await _context.Departments.FindAsync([id], ct);
        if (entity == null)
            return Result.Failure($"Отдел с ID {id} не найден");

        if (string.IsNullOrWhiteSpace(dto.Name))
            return Result.Failure("Название обязательно");

        if (dto.ParentDepartmentId == id)
            return Result.Failure("Отдел не может быть родителем самому себе");

        if (dto.ParentDepartmentId.HasValue)
        {
            var childIds = await GetAllChildIdsAsync(id, ct);
            if (childIds.Contains(dto.ParentDepartmentId.Value))
                return Result.Failure("Нельзя установить дочерний отдел как родительский");
        }

        if (dto.Head.HasValue)
        {
            var headExists = await _context.Users
                .AnyAsync(u => u.Id == dto.Head.Value, ct);
            if (!headExists)
                return Result.Failure("Указанный пользователь не существует");
        }

        entity.Name = dto.Name.Trim();
        entity.ParentDepartmentId = dto.ParentDepartmentId;
        entity.HeadId = dto.Head;

        await SaveChangesAsync(ct);

        _logger.LogInformation("Отдел обновлён: {DepartmentId}", id);
        return Result.Success();
    }

    public async Task<Result> DeleteDepartmentAsync(int id, CancellationToken ct = default)
    {
        var entity = await _context.Departments.FindAsync([id], ct);
        if (entity == null)
            return Result.Failure($"Отдел с ID {id} не найден");

        if (await _context.Departments.AnyAsync(d => d.ParentDepartmentId == id, ct))
            return Result.Failure("Нельзя удалить отдел с дочерними отделами");

        if (await _context.Users.AnyAsync(u => u.DepartmentId == id, ct))
            return Result.Failure("Нельзя удалить отдел с сотрудниками");

        _context.Departments.Remove(entity);
        await SaveChangesAsync(ct);

        _logger.LogInformation("Отдел удалён: {DepartmentId}", id);
        return Result.Success();
    }

    public async Task<Result<List<UserDto>>> GetDepartmentMembersAsync(int departmentId, CancellationToken ct = default)
    {
        var exists = await _context.Departments.AnyAsync(d => d.Id == departmentId, ct);
        if (!exists)
            return Result<List<UserDto>>.Failure($"Отдел с ID {departmentId} не найден");

        var users = await _context.Users.Where(u => u.DepartmentId == departmentId)
            .Include(u => u.Department).AsNoTracking().ToListAsync(ct);

        var result = users.ConvertAll(u => new UserDto
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
        });

        return Result<List<UserDto>>.Success(result);
    }

    public async Task<Result> AddUserToDepartmentAsync(int departmentId, int userId, int requesterId,CancellationToken ct = default)
    {
        var canManage = await CanManageDepartmentInternalAsync(requesterId, departmentId, ct);
        if (!canManage)
            return Result.Failure("Нет прав на управление отделом");

        if (!await _context.Departments.AnyAsync(d => d.Id == departmentId, ct))
            return Result.Failure($"Отдел с ID {departmentId} не найден");

        var user = await _context.Users.FindAsync([userId], ct);
        if (user == null)
            return Result.Failure($"Пользователь с ID {userId} не найден");

        if (user.DepartmentId == departmentId)
            return Result.Failure("Пользователь уже в этом отделе");

        if (user.DepartmentId.HasValue && !await IsAdminAsync(requesterId, ct))
            return Result.Failure("Только администратор может перемещать между отделами");

        user.DepartmentId = departmentId;
        await SaveChangesAsync(ct);

        _logger.LogInformation("Пользователь {UserId} добавлен в отдел {DepartmentId}",userId, departmentId);

        return Result.Success();
    }

    public async Task<Result> RemoveUserFromDepartmentAsync(int departmentId, int userId, int requesterId,CancellationToken ct = default)
    {
        var canManage = await CanManageDepartmentInternalAsync(requesterId, departmentId, ct);
        if (!canManage)
            return Result.Failure("Нет прав на управление отделом");

        var user = await _context.Users.FindAsync([userId], ct);
        if (user == null)
            return Result.Failure($"Пользователь с ID {userId} не найден");

        if (user.DepartmentId != departmentId)
            return Result.Failure("Пользователь не в этом отделе");

        var department = await _context.Departments.FindAsync([departmentId], ct);
        if (department?.HeadId == userId)
            return Result.Failure("Сначала назначьте другого начальника");

        user.DepartmentId = null;
        await SaveChangesAsync(ct);

        _logger.LogInformation("Пользователь {UserId} удалён из отдела {DepartmentId}", userId, departmentId);

        return Result.Success();
    }

    public async Task<Result<bool>> CanManageDepartmentAsync(int userId, int departmentId, CancellationToken ct = default)
    {
        var canManage = await CanManageDepartmentInternalAsync(userId, departmentId, ct);
        return Result<bool>.Success(canManage);
    }

    #region Private

    private async Task<bool> CanManageDepartmentInternalAsync(int userId, int departmentId, CancellationToken ct)
    {
        if (await IsAdminAsync(userId, ct))
            return true;

        return await _context.Departments.AnyAsync(d => d.Id == departmentId && d.HeadId == userId, ct);
    }

    private async Task<bool> IsAdminAsync(int userId, CancellationToken ct)
        => await _context.Users.AnyAsync(u => u.Id == userId && u.DepartmentId == _settings.AdminDepartmentId, ct);

    private async Task<HashSet<int>> GetAllChildIdsAsync(int departmentId, CancellationToken ct)
    {
        var allDepartments = await _context.Departments.AsNoTracking().Select(d => new { d.Id, d.ParentDepartmentId }).ToListAsync(ct);

        var children = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(departmentId);

        while (queue.Count > 0)
        {
            var parentId = queue.Dequeue();
            foreach (var childId in allDepartments.Where(d => d.ParentDepartmentId == parentId).Select(d => d.Id))
            {
                if (children.Add(childId))
                    queue.Enqueue(childId);
            }
        }

        return children;
    }

    #endregion
}