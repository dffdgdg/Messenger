using MessengerAPI.Services.Base;
using MessengerShared.Dto.Department;

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

public sealed partial class DepartmentService(MessengerDbContext context, IOptions<MessengerSettings> settings, ILogger<DepartmentService> logger)
    : BaseService<DepartmentService>(context, logger), IDepartmentService
{
    private readonly MessengerSettings _settings = settings.Value;

    public async Task<Result<List<DepartmentDto>>> GetDepartmentsAsync(CancellationToken ct = default)
    {
        var departments = await _context.Departments.Include(d => d.Head).AsNoTracking().ToListAsync(ct);

        var userCounts = await _context.Users.Where(u => u.Department != null).GroupBy(u => u.DepartmentId)
            .Select(g => new { DepartmentId = g.Key!.Value, Count = g.Count() }).ToDictionaryAsync(x => x.DepartmentId, x => x.Count, ct);

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
        var department = await _context.Departments.Include(d => d.Head).AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);

        if (department is null)
            return Result<DepartmentDto>.NotFound($"Отдел с ID {id} не найден");

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
                return Result<DepartmentDto>.NotFound("Родительский отдел не существует");
        }

        if (dto.Head.HasValue)
        {
            var headExists = await _context.Users.AnyAsync(u => u.Id == dto.Head.Value, ct);
            if (!headExists)
                return Result<DepartmentDto>.NotFound("Указанный пользователь не существует");
        }

        var entity = new Model.Department
        {
            Name = dto.Name.Trim(),
            ParentDepartmentId = dto.ParentDepartmentId,
            HeadId = dto.Head
        };

        _context.Departments.Add(entity);

        var saveResult = await SaveChangesAsync(ct);
        if (saveResult.IsFailure)
            return Result<DepartmentDto>.FromFailure(saveResult);

        LogDepartmentCreated(entity.Id, entity.Name);

        dto.Id = entity.Id;
        dto.UserCount = 0;

        return Result<DepartmentDto>.Success(dto);
    }

    public async Task<Result> UpdateDepartmentAsync(int id, DepartmentDto dto, CancellationToken ct = default)
    {
        var entityResult = await FindEntityAsync<Model.Department>(id, ct);
        if (entityResult.IsFailure)
            return Result.FromFailure(entityResult);

        var entity = entityResult.Value!;

        if (string.IsNullOrWhiteSpace(dto.Name))
            return Result.Failure("Название обязательно");

        if (dto.ParentDepartmentId == id)
            return Result.Failure("Отдел не может быть родителем самому себе");

        if (dto.ParentDepartmentId.HasValue)
        {
            var cycleCheck = await CheckNoCycleAsync(id, dto.ParentDepartmentId.Value, ct);
            if (cycleCheck.IsFailure)
                return cycleCheck;
        }

        if (dto.Head.HasValue)
        {
            var headExists = await _context.Users.AnyAsync(u => u.Id == dto.Head.Value, ct);
            if (!headExists)
                return Result.NotFound("Указанный пользователь не существует");
        }

        entity.Name = dto.Name.Trim();
        entity.ParentDepartmentId = dto.ParentDepartmentId;
        entity.HeadId = dto.Head;

        var saveResult = await SaveChangesAsync(ct);
        if (saveResult.IsFailure)
            return saveResult;

        LogDepartmentUpdated(id);
        return Result.Success();
    }

    public async Task<Result> DeleteDepartmentAsync(int id, CancellationToken ct = default)
    {
        var entityResult = await FindEntityAsync<Model.Department>(id, ct);
        if (entityResult.IsFailure)
            return Result.FromFailure(entityResult);

        if (await _context.Departments.AnyAsync(d => d.ParentDepartmentId == id, ct))
            return Result.Failure("Нельзя удалить отдел с дочерними отделами");

        if (await _context.Users.AnyAsync(u => u.DepartmentId == id, ct))
            return Result.Failure("Нельзя удалить отдел с сотрудниками");

        _context.Departments.Remove(entityResult.Value!);

        var saveResult = await SaveChangesAsync(ct);
        if (saveResult.IsFailure)
            return saveResult;

        LogDepartmentDeleted(id);
        return Result.Success();
    }

    public async Task<Result<List<UserDto>>> GetDepartmentMembersAsync(int departmentId, CancellationToken ct = default)
    {
        var exists = await _context.Departments.AnyAsync(d => d.Id == departmentId, ct);
        if (!exists)
            return Result<List<UserDto>>.NotFound($"Отдел с ID {departmentId} не найден");

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

    public async Task<Result> AddUserToDepartmentAsync(int departmentId, int userId, int requesterId, CancellationToken ct = default)
    {
        var canManageResult = await CheckCanManageAsync(requesterId, departmentId, ct);
        if (canManageResult.IsFailure)
            return canManageResult;

        if (!await _context.Departments.AnyAsync(d => d.Id == departmentId, ct))
            return Result.NotFound($"Отдел с ID {departmentId} не найден");

        var userResult = await FindEntityAsync<Model.User>(userId, ct);
        if (userResult.IsFailure)
            return Result.FromFailure(userResult);

        var user = userResult.Value!;

        if (user.DepartmentId == departmentId)
            return Result.Conflict("Пользователь уже в этом отделе");

        if (user.DepartmentId.HasValue && !await IsAdminAsync(requesterId, ct))
            return Result.Forbidden("Только администратор может перемещать между отделами");

        user.DepartmentId = departmentId;

        var saveResult = await SaveChangesAsync(ct);
        if (saveResult.IsFailure)
            return saveResult;

        LogUserAddedToDepartment(userId, departmentId);

        return Result.Success();
    }

    public async Task<Result> RemoveUserFromDepartmentAsync(int departmentId, int userId, int requesterId, CancellationToken ct = default)
    {
        var canManageResult = await CheckCanManageAsync(requesterId, departmentId, ct);
        if (canManageResult.IsFailure)
            return canManageResult;

        var userResult = await FindEntityAsync<Model.User>(userId, ct);
        if (userResult.IsFailure)
            return Result.FromFailure(userResult);

        var user = userResult.Value!;

        if (user.DepartmentId != departmentId)
            return Result.Failure("Пользователь не в этом отделе");

        var department = await _context.Departments.FindAsync([departmentId], ct);
        if (department?.HeadId == userId)
            return Result.Failure("Сначала назначьте другого начальника");

        user.DepartmentId = null;

        var saveResult = await SaveChangesAsync(ct);
        if (saveResult.IsFailure)
            return saveResult;

        LogUserRemovedFromDepartment(userId, departmentId);

        return Result.Success();
    }

    public async Task<Result<bool>> CanManageDepartmentAsync(int userId, int departmentId, CancellationToken ct = default)
        => Result<bool>.Success(await CanManageDepartmentInternalAsync(userId, departmentId, ct));

    #region Private

    private async Task<Result> CheckCanManageAsync(int userId, int departmentId, CancellationToken ct)
    {
        var canManage = await CanManageDepartmentInternalAsync(userId, departmentId, ct);
        if (!canManage)
            return Result.Forbidden("Нет прав на управление отделом");
        return Result.Success();
    }

    private async Task<bool> CanManageDepartmentInternalAsync(int userId, int departmentId, CancellationToken ct)
    {
        if (await IsAdminAsync(userId, ct))
            return true;

        return await _context.Departments.AnyAsync(d => d.Id == departmentId && d.HeadId == userId, ct);
    }

    private async Task<bool> IsAdminAsync(int userId, CancellationToken ct)
        => await _context.Users.AnyAsync(u => u.Id == userId && u.DepartmentId == _settings.AdminDepartmentId, ct);

    private async Task<Result> CheckNoCycleAsync(int departmentId, int parentId, CancellationToken ct)
    {
        var allDepartments = await _context.Departments.AsNoTracking().Select(d => new { d.Id, d.ParentDepartmentId }).ToListAsync(ct);

        var visited = new HashSet<int> { departmentId };
        var queue = new Queue<int>();
        queue.Enqueue(parentId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (!visited.Add(current))
                return Result.Failure("Нельзя установить дочерний отдел как родительский — обнаружен цикл");

            var parent = allDepartments.FirstOrDefault(d => d.Id == current);
            if (parent?.ParentDepartmentId.HasValue == true)
                queue.Enqueue(parent.ParentDepartmentId.Value);
        }

        return Result.Success();
    }

    #endregion

    #region Log Messages

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Отдел создан: {DepartmentId} '{Name}'")]
    private partial void LogDepartmentCreated(int departmentId, string name);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Отдел обновлён: {DepartmentId}")]
    private partial void LogDepartmentUpdated(int departmentId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Отдел удалён: {DepartmentId}")]
    private partial void LogDepartmentDeleted(int departmentId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Пользователь {UserId} добавлен в отдел {DepartmentId}")]
    private partial void LogUserAddedToDepartment(int userId, int departmentId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Пользователь {UserId} удалён из отдела {DepartmentId}")]
    private partial void LogUserRemovedFromDepartment(int userId, int departmentId);

    #endregion
}