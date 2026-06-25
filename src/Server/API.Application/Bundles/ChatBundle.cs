using API.Application.Services.Abstractions;

namespace API.Application.Bundles;

public sealed record ChatBundle(ISystemMessageService SystemMessages, CacheBundle Cache, TimeBundle Time);
