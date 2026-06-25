using API.Application.Services.Abstractions;

namespace API.Application.Bundles;

public sealed record CacheBundle(IAccessControlService AccessControl, ICacheService CacheService);
