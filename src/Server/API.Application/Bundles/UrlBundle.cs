using API.Application.Services.Abstractions;

namespace API.Application.Bundles;

public sealed record UrlBundle(IUrlBuilder UrlBuilder);
