using API.Application.Configuration;
using API.Application.Services.Abstractions;
using API.Application.Services.Base;
using API.Domain.Common;
using API.Domain.Entities;
using API.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Dto.Chat;
using Shared.Dto.Department;
using Shared.Dto.User;
using Shared.Enum;
using Shared.Hubs;

namespace API.Application.Services.Features.Department;

public sealed partial class DepartmentService(
    IUnitOfWork unitOfWork,
    IOptions<MessengerSettings> settings,
    AppDateTime appDateTime,
    IHubNotifier hubNotifier,
    ICacheService cache,
    IDepartmentRepository departmentRepository,
    IChatRepository chatRepository,
    IUserRepository userRepository,
    ILogger<DepartmentService> logger) : BaseService<DepartmentService>(unitOfWork, logger), IDepartmentService
{
    private readonly MessengerSettings _settings = settings.Value;
    private readonly AppDateTime _appDateTime = appDateTime;
    private readonly IHubNotifier _hubNotifier = hubNotifier;
    private readonly ICacheService _cache = cache;
    private readonly IDepartmentRepository _departmentRepository = departmentRepository;
    private readonly IChatRepository _chatRepository = chatRepository;
    private readonly IUserRepository _userRepository = userRepository;

    public async Task<Result<List<DepartmentDto>>> GetDepartmentsAsync(CancellationToken ct = default)
    {
        var departments = await _departmentRepository.GetAllWithHeadAsync(ct);
        var userCounts = await _departmentRepository.GetUserCountsAsync(ct);

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
        var department = await _departmentRepository.FindByIdWithHeadAsync(id, ct);

        if (department is null)
            return Result<DepartmentDto>.NotFound($"Отдел с ID {id} не найден");

        var userCount = await _departmentRepository.CountUsersAsync(id, ct);

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
            var parentExists = await _departmentRepository.ExistsAsync(dto.ParentDepartmentId.Value, ct);
            if (!parentExists)
                return Result<DepartmentDto>.NotFound("Родительский отдел не существует");
        }

        if (dto.Head.HasValue)
        {
            var headExists = await _userRepository.ExistsAsync(dto.Head.Value, ct);
            if (!headExists)
                return Result<DepartmentDto>.NotFound("Указанный пользователь не существует");
        }

        var entity = new Domain.Entities.Department
        {
            Name = dto.Name.Trim(),
            ParentDepartmentId = dto.ParentDepartmentId,
            HeadId = dto.Head
        };

        var departmentChat = new Domain.Entities.Chat
        {
            Name = $"Отдел {entity.Name}",
            Type = ChatType.Department,
            CreatedById = 1,
            CreatedAt = _appDateTime.UtcNow
        };

        _chatRepository.Add(departmentChat);
        _departmentRepository.Add(entity);

        var save = await SaveChangesAsync(ct);
        if (save.IsFailure) return save.As<DepartmentDto>();

        entity.ChatId = departmentChat.Id;
        var linkSave = await SaveChangesAsync(ct);
        if (linkSave.IsFailure) return linkSave.As<DepartmentDto>();

        if (dto.Head.HasValue)
        {
            var headUser = await _userRepository.FindByIdTrackedAsync(dto.Head.Value, ct);
            if (headUser != null)
            {
                var oldDeptId = headUser.DepartmentId;
                headUser.DepartmentId = entity.Id;
                var sync = await SyncDepartmentChatMembershipAsync(headUser.Id, oldDeptId, entity.Id, ct);
                if (sync.IsFailure) return sync.As<DepartmentDto>();

                await NotifyUserRoleAsync(headUser.Id, ct);
            }
        }

        await SyncHeadsChatMembershipAsync(null, entity.HeadId, ct);

        var headsSave = await SaveChangesAsync(ct);
        if (headsSave.IsFailure) return headsSave.As<DepartmentDto>();

        LogDepartmentCreated(entity.Id, entity.Name);

        dto.Id = entity.Id;
        dto.UserCount = 0;

        return Result<DepartmentDto>.Success(dto);
    }

    public async Task<Result> UpdateDepartmentAsync(int id, DepartmentDto dto, CancellationToken ct = default)
    {
        var entity = await _departmentRepository.FindByIdAsync(id, ct);
        if (entity is null)
            return Result.NotFound($"Отдел с ID {id} не найден");

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
            var headExists = await _userRepository.ExistsAsync(dto.Head.Value, ct);
            if (!headExists)
                return Result.NotFound("Указанный пользователь не существует");
        }

        var oldHeadId = entity.HeadId;
        var oldName = entity.Name;

        if (dto.Head.HasValue && dto.Head != oldHeadId)
        {
            var departments = await _departmentRepository.GetAllAsync(ct);
            var otherDepartment = departments.FirstOrDefault(d => d.HeadId == dto.Head.Value && d.Id != id);

            if (otherDepartment != null)
            {
                var otherOldHeadId = otherDepartment.HeadId;
                otherDepartment.HeadId = null;

                var interimSave = await SaveChangesAsync(ct);
                if (interimSave.IsFailure) return interimSave;

                if (otherOldHeadId.HasValue)
                    await NotifyUserRoleAsync(otherOldHeadId.Value, ct);

                await SyncHeadsChatMembershipAsync(otherOldHeadId, null, ct, excludeDepartmentId: otherDepartment.Id);
            }
        }

        entity.Name = dto.Name.Trim();
        entity.ParentDepartmentId = dto.ParentDepartmentId;
        entity.HeadId = dto.Head;

        if (dto.Head.HasValue && dto.Head != oldHeadId)
        {
            var headUser = await _userRepository.FindByIdTrackedAsync(dto.Head.Value, ct);
            if (headUser != null && headUser.DepartmentId != id)
            {
                var oldUserDeptId = headUser.DepartmentId;
                headUser.DepartmentId = id;
                var sync = await SyncDepartmentChatMembershipAsync(headUser.Id, oldUserDeptId, id, ct);
                if (sync.IsFailure) return sync;

                await NotifyUserRoleAsync(headUser.Id, ct);
            }
        }

        if (entity.ChatId.HasValue && !string.Equals(oldName, entity.Name, StringComparison.Ordinal))
        {
            var chat = await _chatRepository.GetChatWithTrackingAsync(entity.ChatId.Value, ct);
            if (chat is not null)
                chat.Name = $"Отдел {entity.Name}";
        }

        await SyncHeadsChatMembershipAsync(oldHeadId, entity.HeadId, ct, excludeDepartmentId: entity.Id);

        var save = await SaveChangesAsync(ct);
        if (save.IsFailure) return save;

        if (oldHeadId.HasValue && oldHeadId != entity.HeadId)
            await NotifyUserRoleAsync(oldHeadId.Value, ct);

        if (entity.HeadId.HasValue && entity.HeadId != oldHeadId)
            await NotifyUserRoleAsync(entity.HeadId.Value, ct);

        LogDepartmentUpdated(id);
        return Result.Success();
    }

    public async Task<Result> DeleteDepartmentAsync(int id, CancellationToken ct = default)
    {
        var entity = await _departmentRepository.FindByIdAsync(id, ct);
        if (entity is null)
            return Result.NotFound($"Отдел с ID {id} не найден");

        if (await _departmentRepository.HasChildDepartmentsAsync(id, ct))
            return Result.Failure("Нельзя удалить отдел с дочерними отделами");

        if (await _departmentRepository.HasUsersAsync(id, ct))
            return Result.Failure("Нельзя удалить отдел с сотрудниками");

        await SyncHeadsChatMembershipAsync(entity.HeadId, null, ct, excludeDepartmentId: entity.Id);

        if (entity.ChatId.HasValue)
        {
            var chat = await _chatRepository.GetChatWithTrackingAsync(entity.ChatId.Value, ct);
            if (chat is not null)
                _chatRepository.Remove(chat);
        }

        _departmentRepository.Remove(entity);

        var save = await SaveChangesAsync(ct);
        if (save.IsFailure) return save;

        LogDepartmentDeleted(id);
        return Result.Success();
    }

    public async Task<Result<List<UserDto>>> GetDepartmentMembersAsync(int departmentId, CancellationToken ct = default)
    {
        var exists = await _departmentRepository.ExistsAsync(departmentId, ct);
        if (!exists)
            return Result<List<UserDto>>.NotFound($"Отдел с ID {departmentId} не найден");

        var users = await _userRepository.GetByIdsAsync(
            await _userRepository.GetUserIdsByDepartmentAsync(departmentId, ct), ct);

        var result = users.Select(u => new UserDto
        {
            Id = u.Id,
            Username = u.Username,
            DisplayName = (u.Surname + " " + u.Name + (u.Midname != null ? " " + u.Midname : "")).Trim(),
            Surname = u.Surname,
            Name = u.Name,
            Midname = u.Midname,
            Avatar = u.Avatar,
            DepartmentId = u.DepartmentId,
            Department = u.Department?.Name
        }).ToList();

        return Result<List<UserDto>>.Success(result);
    }

    public async Task<Result> AddUserToDepartmentAsync(int departmentId, int userId, int requesterId, CancellationToken ct = default)
    {
        var canManageResult = await CheckCanManageAsync(requesterId, departmentId, ct);
        if (canManageResult.IsFailure) return canManageResult;

        if (!await _departmentRepository.ExistsAsync(departmentId, ct))
            return Result.NotFound($"Отдел с ID {departmentId} не найден");

        var user = await _userRepository.FindByIdTrackedAsync(userId, ct);
        if (user is null)
            return Result.NotFound($"Пользователь с ID {userId} не найден");

        if (user.DepartmentId == departmentId)
            return Result.Conflict("Пользователь уже в этом отделе");

        if (user.DepartmentId.HasValue && !await IsAdminAsync(requesterId, ct))
            return Result.Forbidden("Только администратор может перемещать между отделами");

        var oldDepartmentId = user.DepartmentId;
        user.DepartmentId = departmentId;

        var sync = await SyncDepartmentChatMembershipAsync(userId, oldDepartmentId, departmentId, ct);
        if (sync.IsFailure) return sync;

        await NotifyUserRoleAsync(userId, ct);
        LogUserAddedToDepartment(userId, departmentId);

        return Result.Success();
    }

    public async Task<Result> RemoveUserFromDepartmentAsync(int departmentId, int userId, int requesterId, CancellationToken ct = default)
    {
        var canManageResult = await CheckCanManageAsync(requesterId, departmentId, ct);
        if (canManageResult.IsFailure) return canManageResult;

        var user = await _userRepository.FindByIdTrackedAsync(userId, ct);
        if (user is null)
            return Result.NotFound($"Пользователь с ID {userId} не найден");

        if (user.DepartmentId != departmentId)
            return Result.Failure("Пользователь не в этом отделе");

        var department = await _departmentRepository.FindByIdAsync(departmentId, ct);
        if (department?.HeadId == userId)
            return Result.Failure("Сначала назначьте другого начальника");

        var oldDepartmentId = user.DepartmentId;
        user.DepartmentId = null;

        var sync = await SyncDepartmentChatMembershipAsync(userId, oldDepartmentId, null, ct);
        if (sync.IsFailure) return sync;

        await NotifyUserRoleAsync(userId, ct);
        LogUserRemovedFromDepartment(userId, departmentId);

        return Result.Success();
    }

    public async Task<Result<bool>> CanManageDepartmentAsync(int userId, int departmentId, CancellationToken ct = default)
        => Result<bool>.Success(await CanManageDepartmentInternalAsync(userId, departmentId, ct));

    public async Task<Result> SyncDepartmentChatMembershipAsync(int userId, int? oldDepartmentId, int? newDepartmentId, CancellationToken ct = default)
    {
        var sync = await BuildDepartmentChatMembershipSyncAsync(userId, oldDepartmentId, newDepartmentId, ct);

        var save = await SaveChangesAsync(ct);
        if (save.IsFailure) return save;

        await NotifyDepartmentChatMembershipChangedAsync(userId, sync, ct);
        return Result.Success();
    }

    #region Private

    private async Task NotifyUserRoleAsync(int userId, CancellationToken ct)
    {
        var role = await _departmentRepository.ResolveUserRoleAsync(userId, _settings.AdminDepartmentId, ct);
        await _hubNotifier.SendToUserAsync(userId, HubMethods.Chat.UserRoleUpdated, role);
    }

    private async Task SyncHeadsChatMembershipAsync(int? oldHeadId, int? newHeadId, CancellationToken ct, int? excludeDepartmentId = null)
    {
        if (oldHeadId == newHeadId)
            return;

        var headsChatSetting = await _departmentRepository.GetSystemSettingAsync("heads_chat_id", ct);

        if (headsChatSetting is null || !int.TryParse(headsChatSetting.Value, out var headsChatId))
            return;

        if (oldHeadId.HasValue)
        {
            var stillManages = excludeDepartmentId.HasValue
                ? await _departmentRepository.IsHeadOfDepartmentAsync(oldHeadId.Value, excludeDepartmentId.Value, ct)
                : await _departmentRepository.IsHeadOfAnyDepartmentAsync(oldHeadId.Value, ct);

            if (!stillManages)
            {
                var oldMember = await _chatRepository.GetMemberAsync(headsChatId, oldHeadId.Value, ct);
                if (oldMember is not null)
                    _chatRepository.RemoveMember(oldMember);
            }
        }

        if (newHeadId.HasValue)
        {
            var exists = await _chatRepository.IsMemberAsync(headsChatId, newHeadId.Value, ct);
            if (!exists)
            {
                _chatRepository.AddMember(new ChatMember
                {
                    ChatId = headsChatId,
                    UserId = newHeadId.Value,
                    JoinedAt = _appDateTime.UtcNow,
                    NotificationsEnabled = true
                });
            }
        }
    }

    private async Task<DepartmentChatSyncResult> BuildDepartmentChatMembershipSyncAsync(int userId, int? oldDepartmentId, int? newDepartmentId, CancellationToken ct)
    {
        if (oldDepartmentId == newDepartmentId)
            return DepartmentChatSyncResult.Empty;

        var departmentIds = new[] { oldDepartmentId, newDepartmentId }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (departmentIds.Count == 0)
            return DepartmentChatSyncResult.Empty;

        var chatMap = await _departmentRepository.GetChatIdsForDepartmentsAsync(departmentIds, ct);

        int? removedChatId = null;
        ChatUpdateEventDto? addedChat = null;

        if (oldDepartmentId.HasValue && chatMap.TryGetValue(oldDepartmentId.Value, out var oldChat))
        {
            removedChatId = oldChat.ChatId;
            var oldMember = await _chatRepository.GetMemberAsync(oldChat.ChatId, userId, ct);

            if (oldMember is not null)
                _chatRepository.RemoveMember(oldMember);
        }

        if (newDepartmentId.HasValue && chatMap.TryGetValue(newDepartmentId.Value, out var newChat))
        {
            var exists = await _chatRepository.IsMemberAsync(newChat.ChatId, userId, ct);

            if (!exists)
            {
                _chatRepository.AddMember(new ChatMember
                {
                    ChatId = newChat.ChatId,
                    UserId = userId,
                    Role = ChatRole.Member,
                    JoinedAt = _appDateTime.UtcNow,
                    NotificationsEnabled = true
                });
            }

            addedChat = new ChatUpdateEventDto
            {
                Id = newChat.ChatId,
                Name = newChat.ChatName,
                Type = newChat.ChatType,
                CreatedById = newChat.CreatedById ?? 0,
                Avatar = newChat.Avatar,
                ShowHistoryForNewMembers = newChat.ShowHistoryForNewMembers,
                CurrentUserRole = ChatRole.Member
            };
        }

        return new DepartmentChatSyncResult(removedChatId, addedChat);
    }

    private async Task NotifyDepartmentChatMembershipChangedAsync(int userId, DepartmentChatSyncResult sync, CancellationToken ct)
    {
        if (sync.RemovedChatId.HasValue)
        {
            _cache.InvalidateMembership(userId, sync.RemovedChatId.Value);
            await _hubNotifier.SendToUserAsync(userId, HubMethods.Chat.ChatRemoved, sync.RemovedChatId.Value);
            await _hubNotifier.RemoveUserFromChatGroupAsync(userId, sync.RemovedChatId.Value);
        }

        if (sync.AddedChat is not null)
        {
            _cache.InvalidateMembership(userId, sync.AddedChat.Id);
            await _hubNotifier.SendToUserAsync(userId, HubMethods.Chat.ChatUpdated, sync.AddedChat);
            await _hubNotifier.AddUserToChatGroupAsync(userId, sync.AddedChat.Id);
        }
    }

    private sealed record DepartmentChatSyncResult(int? RemovedChatId, ChatUpdateEventDto? AddedChat)
    {
        public static DepartmentChatSyncResult Empty { get; } = new(null, null);
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

        return await _departmentRepository.IsHeadOfDepartmentAsync(userId, departmentId, ct);
    }

    private async Task<bool> IsAdminAsync(int userId, CancellationToken ct)
        => await _departmentRepository.ResolveUserRoleAsync(userId, _settings.AdminDepartmentId, ct) != UserRole.User
           && await _departmentRepository.ResolveUserRoleAsync(userId, _settings.AdminDepartmentId, ct) == (UserRole.User | UserRole.Admin);

    private async Task<Result> CheckNoCycleAsync(int departmentId, int parentId, CancellationToken ct)
    {
        var allDepartments = await _departmentRepository.GetAllAsync(ct);

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