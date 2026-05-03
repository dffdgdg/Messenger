namespace API.Services.Abstractions;

public interface IUrlBuilder
{
    string? BuildUrl(string? relativePath);
}