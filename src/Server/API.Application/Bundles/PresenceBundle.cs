using API.Application.Services.Abstractions;

namespace API.Application.Bundles;

public sealed record PresenceBundle(IOnlineUserService OnlineService);
