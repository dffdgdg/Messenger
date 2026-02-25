namespace MessengerAPI.Services.Infrastructure;

public interface IUrlBuilder
{
    string? BuildUrl(string? relativePath);
}

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