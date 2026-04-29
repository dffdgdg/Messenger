namespace MessengerAPI.Services.Abstractions;

public interface IUrlBuilder
{
    string? BuildUrl(string? relativePath);
}