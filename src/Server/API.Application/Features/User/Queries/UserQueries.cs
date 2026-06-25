using Microsoft.AspNetCore.Http;
using Shared.Contracts.User;

namespace API.Application.Features.User.Queries;

public sealed record GetAllUsersQuery;
public sealed record GetUserQuery(int UserId);
public sealed record GetOnlineUsersQuery;
public sealed record GetUserStatusQuery(int UserId);
public sealed record GetUserStatusesQuery(List<int> UserIds);
