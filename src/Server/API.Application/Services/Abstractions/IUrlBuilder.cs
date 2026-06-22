namespace API.Application.Services.Abstractions;

public interface IUrlBuilder
{
    string? BuildUrl(string? relativePath);
}