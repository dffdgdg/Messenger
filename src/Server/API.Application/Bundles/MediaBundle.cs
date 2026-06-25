using API.Application.Services.Abstractions;

namespace API.Application.Bundles;

public sealed record MediaBundle(IFileService FileService);
