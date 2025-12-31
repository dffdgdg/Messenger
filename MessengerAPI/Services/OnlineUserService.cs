using System.Collections.Concurrent;

namespace MessengerAPI.Services
{
    public interface IOnlineUserService
    {
        void UserConnected(int userId, string connectionId);
        void UserDisconnected(int userId, string connectionId);
        bool IsUserOnline(int userId);
        bool IsOnline(int userId);
        HashSet<int> GetOnlineUserIds();
        HashSet<int> FilterOnlineUserIds(IEnumerable<int> userIds);
        HashSet<int> FilterOnline(IEnumerable<int> userIds);
        int OnlineCount { get; }
    }

    public class OnlineUserService : IOnlineUserService
    {
        private readonly ConcurrentDictionary<int, ConcurrentDictionary<string, byte>> _connections = new();

        public void UserConnected(int userId, string connectionId)
        {
            var userConnections = _connections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>());
            userConnections.TryAdd(connectionId, 0);
        }

        public void UserDisconnected(int userId, string connectionId)
        {
            if (!_connections.TryGetValue(userId, out var userConnections))
                return;

            userConnections.TryRemove(connectionId, out _);

            // Удаляем пользователя, если нет активных подключений
            if (userConnections.IsEmpty)
            {
                _connections.TryRemove(userId, out _);
            }
        }

        public bool IsUserOnline(int userId) => IsOnline(userId);

        public bool IsOnline(int userId) => _connections.TryGetValue(userId, out var connections) && !connections.IsEmpty;

        public HashSet<int> GetOnlineUserIds() => [.. _connections.Where(kv => !kv.Value.IsEmpty).Select(kv => kv.Key)];

        public HashSet<int> FilterOnlineUserIds(IEnumerable<int> userIds) => FilterOnline(userIds);

        public HashSet<int> FilterOnline(IEnumerable<int> userIds) => [.. userIds.Where(IsOnline)];

        public int OnlineCount => _connections.Count(kv => !kv.Value.IsEmpty);
    }
}