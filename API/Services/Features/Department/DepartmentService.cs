using API.Services.Base;

namespace API.Services.Department;

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
            HeadName = d.Head?.GetDisplayName(),
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
            HeadName = department.Head?.GetDisplayName(),
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

        var entity = new Data.Department
        {
            Name = dto.Name.Trim(),
            ParentDepartmentId = dto.ParentDepartmentId,
            HeadId = dto.Head
        };

        var departmentChat = new Data.Chat
        {
            Name = $"Отдел {entity.Name}",
            Type = ChatType.Department,
            CreatedById = 1,
            CreatedAt = DateTime.UtcNow
        };

        _context.Chats.Add(departmentChat);

        _context.Departments.Add(entity);

        var save = await SaveChangesAsync(ct);
        if (save.IsFailure) return save.As<DepartmentDto>();

        entity.ChatId = departmentChat.Id;
        var linkSave = await SaveChangesAsync(ct);
        if (linkSave.IsFailure) return linkSave.As<DepartmentDto>();

        await SyncHeadsChatMembershipAsync(null, entity.HeadId, ct);


        LogDepartmentCreated(entity.Id, entity.Name);

        dto.Id = entity.Id;
        dto.UserCount = 0;

        return Result<DepartmentDto>.Success(dto);
    }

    public async Task<Result> UpdateDepartmentAsync(int id, DepartmentDto dto, CancellationToken ct = default)
    {
        var entityResult = await FindEntityAsync<Data.Department>(id, ct);
        if (entityResult.IsFailure) return entityResult;

        var entity = entityResult.Value!;

        if (string.IsNullOrWhiteSpace(dto.Name))
            return Result.Failure("Название обязательно");

        if (dto.ParentDepartmentId == id)
            return Result.Failure("Отдел не может быть родителем самому себе");

        if (dto.ParentDepartmentId.HasValue)
        {
            var cycleCheck = await CheckNoCycleAsync(id, dto.ParentDepartmentId.Value, ct);
            if (cycleCheck.IsFailure) return cycleCheck;
        }

        if (dto.Head.HasValue)
        {
            var headExists = await _context.Users.AnyAsync(u => u.Id == dto.Head.Value, ct);
            if (!headExists)
                return Result.NotFound("Указанный пользователь не существует");
        }
        var oldHeadId = entity.HeadId;
        var oldName = entity.Name;

        entity.Name = dto.Name.Trim();
        entity.ParentDepartmentId = dto.ParentDepartmentId;
        entity.HeadId = dto.Head;

        if (entity.ChatId.HasValue && !string.Equals(oldName, entity.Name, StringComparison.Ordinal))
        {
            var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Id == entity.ChatId.Value, ct);
            chat?.Name = $"Отдел {entity.Name}";
        }

        await SyncHeadsChatMembershipAsync(oldHeadId, entity.HeadId, ct, excludeDepartmentId: entity.Id);

        var save = await SaveChangesAsync(ct);
        if (save.IsFailure) return save;

        LogDepartmentUpdated(id);
        return Result.Success();
    }

    public async Task<Result> DeleteDepartmentAsync(int id, CancellationToken ct = default)
    {
        var entityResult = await FindEntityAsync<Data.Department>(id, ct);
        if (entityResult.IsFailure) return entityResult;

        if (await _context.Departments.AnyAsync(d => d.ParentDepartmentId == id, ct))
            return Result.Failure("Нельзя удалить отдел с дочерними отделами");

        if (await _context.Users.AnyAsync(u => u.DepartmentId == id, ct))
            return Result.Failure("Нельзя удалить отдел с сотрудниками");

        var entity = entityResult.Value!;

        await SyncHeadsChatMembershipAsync(entity.HeadId, null, ct, excludeDepartmentId: entity.Id);

        if (entity.ChatId.HasValue)
        {
            var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Id == entity.ChatId.Value, ct);
            if (chat is not null)
                _context.Chats.Remove(chat);
        }

        _context.Departments.Remove(entity);

        var save = await SaveChangesAsync(ct);
        if (save.IsFailure) return save;

        LogDepartmentDeleted(id);
        return Result.Success();
    }

    public async Task<Result<List<UserDto>>> GetDepartmentMembersAsync(int departmentId, CancellationToken ct = default)
    {
        var exists = await _context.Departments.AnyAsync(d => d.Id == departmentId, ct);
        if (!exists)
            return Result<List<UserDto>>.NotFound($"Отдел с ID {departmentId} не найден");

        var result = await _context.Users.Where(u => u.DepartmentId == departmentId)
            .Select(u => new UserDto
            {
                Id = u.Id,
                Username = u.Username,
                DisplayName = (u.Surname + " " + u.Name + (u.Midname != null ? " " + u.Midname : "")).Trim(),
                Surname = u.Surname,
                Name = u.Name,
                Midname = u.Midname,
                Avatar = u.Avatar,
                DepartmentId = u.DepartmentId,
                Department = u.Department != null ? u.Department.Name : null
            })
            .AsNoTracking()
            .ToListAsync(ct);

        return Result<List<UserDto>>.Success(result);
    }

    public async Task<Result> AddUserToDepartmentAsync(int departmentId, int userId, int requesterId, CancellationToken ct = default)
    {
        var canManageResult = await CheckCanManageAsync(requesterId, departmentId, ct);
        if (canManageResult.IsFailure) return canManageResult;

        if (!await _context.Departments.AnyAsync(d => d.Id == departmentId, ct))
            return Result.NotFound($"Отдел с ID {departmentId} не найден");

        var userResult = await FindEntityAsync<Data.User>(userId, ct);
        if (userResult.IsFailure) return userResult;

        var user = userResult.Value!;

        if (user.DepartmentId == departmentId)
            return Result.Conflict("Пользователь уже в этом отделе");

        if (user.DepartmentId.HasValue && !await IsAdminAsync(requesterId, ct))
            return Result.Forbidden("Только администратор может перемещать между отделами");

        var oldDepartmentId = user.DepartmentId;
        user.DepartmentId = departmentId;

        await SyncDepartmentChatMembershipAsync(userId, oldDepartmentId, departmentId, ct);

        var save = await SaveChangesAsync(ct);
        if (save.IsFailure) return save;

        LogUserAddedToDepartment(userId, departmentId);

        return Result.Success();
    }

    public async Task<Result> RemoveUserFromDepartmentAsync(int departmentId, int userId, int requesterId, CancellationToken ct = default)
    {
        var canManageResult = await CheckCanManageAsync(requesterId, departmentId, ct);
        if (canManageResult.IsFailure) return canManageResult;

        var userResult = await FindEntityAsync<Data.User>(userId, ct);
        if (userResult.IsFailure) return userResult;

        var user = userResult.Value!;

        if (user.DepartmentId != departmentId)
            return Result.Failure("Пользователь не в этом отделе");

        var department = await _context.Departments.FindAsync([departmentId], ct);
        if (department?.HeadId == userId)
            return Result.Failure("Сначала назначьте другого начальника");

        var oldDepartmentId = user.DepartmentId;
        user.DepartmentId = null;

        await SyncDepartmentChatMembershipAsync(userId, oldDepartmentId, null, ct);

        var save = await SaveChangesAsync(ct);
        if (save.IsFailure) return save;

        LogUserRemovedFromDepartment(userId, departmentId);

        return Result.Success();
    }

    public async Task<Result<bool>> CanManageDepartmentAsync(int userId, int departmentId, CancellationToken ct = default)
        => Result<bool>.Success(await CanManageDepartmentInternalAsync(userId, departmentId, ct));

    #region Private
    private async Task SyncHeadsChatMembershipAsync(int? oldHeadId, int? newHeadId, CancellationToken ct, int? excludeDepartmentId = null)
    {
        if (oldHeadId == newHeadId)
            return;

        var headsChatSetting = await _context.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == "heads_chat_id", ct);

        if (headsChatSetting is null || !int.TryParse(headsChatSetting.Value, out var headsChatId))
            return;

        if (oldHeadId.HasValue)
        {
            var stillManages = await _context.Departments.AnyAsync(d => d.HeadId == oldHeadId.Value && (!excludeDepartmentId.HasValue || d.Id != excludeDepartmentId.Value), ct);
            if (!stillManages)
            {
                var oldMember = await _context.ChatMembers.FirstOrDefaultAsync(cm => cm.ChatId == headsChatId && cm.UserId == oldHeadId.Value, ct);
                if (oldMember is not null)
                    _context.ChatMembers.Remove(oldMember);
            }
        }

        if (newHeadId.HasValue)
        {
            var exists = await _context.ChatMembers.AnyAsync(cm => cm.ChatId == headsChatId && cm.UserId == newHeadId.Value, ct);
            if (!exists)
            {
                _context.ChatMembers.Add(new ChatMember
                {
                    ChatId = headsChatId,
                    UserId = newHeadId.Value,
                    JoinedAt = DateTime.UtcNow,
                    NotificationsEnabled = true
                });
            }
        }
    }

    private async Task SyncDepartmentChatMembershipAsync(int userId, int? oldDepartmentId, int? newDepartmentId, CancellationToken ct)
    {
        if (oldDepartmentId == newDepartmentId)
            return;

        var departmentIds = new[] { oldDepartmentId, newDepartmentId }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (departmentIds.Count == 0)
            return;

        var chatMap = await _context.Departments
            .Where(d => departmentIds.Contains(d.Id) && d.ChatId.HasValue)
            .Select(d => new { d.Id, ChatId = d.ChatId!.Value })
            .ToDictionaryAsync(x => x.Id, x => x.ChatId, ct);

        if (oldDepartmentId.HasValue && chatMap.TryGetValue(oldDepartmentId.Value, out var oldChatId))
        {
            var oldMember = await _context.ChatMembers
                .FirstOrDefaultAsync(cm => cm.ChatId == oldChatId && cm.UserId == userId, ct);

            if (oldMember is not null)
                _context.ChatMembers.Remove(oldMember);
        }

        if (newDepartmentId.HasValue && chatMap.TryGetValue(newDepartmentId.Value, out var newChatId))
        {
            var exists = await _context.ChatMembers
                .AnyAsync(cm => cm.ChatId == newChatId && cm.UserId == userId, ct);

            if (!exists)
            {
                _context.ChatMembers.Add(new ChatMember
                {
                    ChatId = newChatId,
                    UserId = userId,
                    JoinedAt = DateTime.UtcNow,
                    NotificationsEnabled = true
                });
            }
        }
    }

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

    #region Log

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