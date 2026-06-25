using API.Application.Common;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Domain.Repositories;
using Shared.Contracts.User;

namespace API.Application.Features.Chat.Queries;

public class GetChatMembersQueryHandler(IAccessControlService accessControl, IChatRepository chatRepository,
    IOnlineUserService onlineService, IUrlBuilder urlBuilder)
    : IQueryHandler<GetChatMembersQuery, Result<List<UserDto>>>
{
    public virtual async Task<Result<List<UserDto>>> HandleAsync(GetChatMembersQuery query, CancellationToken ct = default)
    {
        var access = await accessControl.EnsureMemberOfAsync(query.UserId, query.ChatId);
        if (access.IsFailure) return access.As<List<UserDto>>();

        var members = await chatRepository.GetMembersWithUsersAsync(query.ChatId, ct);
        var memberIds = members.ConvertAll(m => m.UserId);
        var onlineIds = onlineService.FilterOnline(memberIds);

        var result = members.ConvertAll(m => new UserDto
        {
            Id = m.UserId,
            Username = m.Username,
            DisplayName = FormatDisplayName(m.Surname, m.Name, m.Midname),
            Surname = m.Surname,
            Name = m.Name,
            Midname = m.Midname,
            Avatar = urlBuilder.BuildUrl(m.Avatar),
            IsOnline = onlineIds.Contains(m.UserId),
            LastOnline = m.LastOnline,
            StatusType = m.StatusType,
            StatusExpiresAt = m.StatusExpiresAt
        });

        return Result<List<UserDto>>.Success(result);
    }

    private static string FormatDisplayName(string? surname, string? name, string? midname)
    {
        var parts = new[] { surname, name, midname }.Where(s => !string.IsNullOrWhiteSpace(s));
        return parts.Any() ? string.Join(" ", parts) : "Без имени";
    }
}

