namespace API.Services.Abstractions;

public interface IOnlineUserService : IDisposable
{
    void UserConnected(int userId, string connectionId);
    void UserDisconnected(int userId, string connectionId);
    bool IsOnline(int userId);
    HashSet<int> GetOnlineUserIds();
    HashSet<int> FilterOnline(IEnumerable<int> userIds);
    int OnlineCount { get; }
}