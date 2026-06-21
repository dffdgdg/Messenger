using API.Application.Services.Abstractions;
using Microsoft.AspNetCore.Http;

namespace API.Infrastructure.Network;

public class HttpUrlBuilder(IHttpContextAccessor httpContextAccessor) : IUrlBuilder
{
    public string? BuildUrl(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return null;

        if (relativePath.StartsWith("http://") || relativePath.StartsWith("https://"))
            return relativePath;

        var request = httpContextAccessor.HttpContext?.Request;
        if (request == null)
            return relativePath;

        var path = relativePath.TrimStart('/');
        return $"{request.Scheme}://{request.Host}/{path}";
    }
}