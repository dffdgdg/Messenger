namespace API.Application.Services.Abstractions;

public interface IOnlineUserService : IDisposable
{
    void UserConnected(int userId, string connectionId);
    void UserDisconnected(int userId, string connectionId);
    bool IsOnline(int userId);
    HashSet<int> GetOnlineUserIds();
    IReadOnlyCollection<string> GetConnectionIds(int userId);
    HashSet<int> FilterOnline(IEnumerable<int> userIds);
    int OnlineCount { get; }
}