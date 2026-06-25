using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Projections;
using API.Domain.Repositories;
using Shared.Contracts.User;
using Shared.Enum;

namespace API.Application.Features.User.Queries;

public class GetAllUsersQueryHandler(IUserRepository userRepository, IOnlineUserService onlineService, IUrlBuilder urlBuilder)
    : IQueryHandler<GetAllUsersQuery, Result<List<UserDto>>>
{
    public virtual async Task<Result<List<UserDto>>> HandleAsync(GetAllUsersQuery query, CancellationToken ct = default)
    {
        var users = await userRepository.GetAllWithSettingsAsync(ct);
        var onlineIds = onlineService.GetOnlineUserIds();

        return Result<List<UserDto>>.Success(users.ConvertAll(u => MapToDto(u, onlineIds.Contains(u.Id))));
    }

    internal UserDto MapToDto(UserWithSettingsProjection u, bool isOnline) => new()
    {
        Id = u.Id,
        Username = u.Username,
        DisplayName = FormatName(u.Surname, u.Name, u.Midname),
        Surname = u.Surname,
        Name = u.Name,
        Midname = u.Midname,
        Avatar = urlBuilder.BuildUrl(u.Avatar),
        DepartmentId = u.DepartmentId,
        Department = u.DepartmentName,
        IsBanned = u.IsBanned,
        LastOnline = u.LastOnline,
        Theme = u.Theme,
        NotificationsEnabled = u.NotificationsEnabled,
        IsOnline = isOnline,
        StatusType = isOnline ? u.StatusType : UserStatusType.Online,
        StatusExpiresAt = u.StatusExpiresAt
    };

    private static string? FormatName(string? surname, string? name, string? midname)
    {
        var parts = new[] { surname, name, midname }.Where(s => !string.IsNullOrWhiteSpace(s));
        return parts.Any() ? string.Join(" ", parts) : null;
    }
}

