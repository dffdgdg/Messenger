using API.Application.Services.Abstractions;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace API.Infrastructure.Services.Features.Messaging;

public sealed class PendingUploadStore : IPendingUploadStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<string, PendingUpload> _store = new();

    public string Register(int userId, int chatId, string absolutePath, string relativePath, string fileName, string contentType, long fileSize)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

        _store[token] = new PendingUpload(token, userId, chatId, absolutePath, relativePath, fileName, contentType, fileSize, DateTime.UtcNow.Add(Ttl));

        return token;
    }

    public bool TryConsume(string token, int userId, int chatId, out PendingUpload? upload)
    {
        if (_store.TryRemove(token, out var found) &&
            found.UserId == userId &&
            found.ChatId == chatId &&
            found.ExpiresAtUtc > DateTime.UtcNow)
        {
            upload = found;
            return true;
        }

        upload = null;
        return false;
    }

    public IReadOnlyCollection<PendingUpload> GetExpired(DateTime utcNow)
        => _store.Values.Where(u => u.ExpiresAtUtc <= utcNow).ToList();

    public void Remove(string token) => _store.TryRemove(token, out _);
}