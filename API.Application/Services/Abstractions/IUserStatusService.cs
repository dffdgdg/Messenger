using API.Domain.Common;
using Shared.Dto.Online;
using Shared.Enum;

namespace API.Application.Services.Abstractions;

public interface IUserStatusService
{
    Task<Result> SetStatusAsync(int userId, UserStatusType statusType, TimeSpan? duration);
    Task<Result<UserStatusDto>> GetStatusAsync(int userId);
    Task CleanupExpiredStatusesAsync();
}