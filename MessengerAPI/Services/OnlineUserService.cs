using System.Collections.Concurrent;

namespace MessengerAPI.Services
{
    public interface IOnlineUserService
    {
        void UserConnected(int userId, string connectionId);
        void UserDisconnected(int userId, string connectionId);
        bool IsUserOnline(int userId);
        HashSet<int> GetOnlineUserIds();
        HashSet<int> FilterOnlineUserIds(IEnumerable<int> userIds);
        int GetOnlineCount();
        IReadOnlyDictionary<int, int> GetConnectionCounts();
    }

    public class OnlineUserService : IOnlineUserService
    {
        private readonly ConcurrentDictionary<int, HashSet<string>> _userConnections = new();
        private readonly object _lock = new();

        public void UserConnected(int userId, string connectionId)
        {
            lock (_lock)
            {
                if (!_userConnections.TryGetValue(userId, out var connections))
                {
                    connections = [];
                    _userConnections[userId] = connections;
                }
                connections.Add(connectionId);
            }
        }

        public void UserDisconnected(int userId, string connectionId)
        {
            lock (_lock)
            {
                if (_userConnections.TryGetValue(userId, out var connections))
                {
                    connections.Remove(connectionId);
                    if (connections.Count == 0)
                    {
                        _userConnections.TryRemove(userId, out _);
                    }
                }
            }
        }

        public bool IsUserOnline(int userId)
        {
            return _userConnections.TryGetValue(userId, out var connections) && connections.Count > 0;
        }

        public HashSet<int> GetOnlineUserIds()
        {
            lock (_lock)
            {
                return [.. _userConnections.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key)];
            }
        }

        public HashSet<int> FilterOnlineUserIds(IEnumerable<int> userIds)
        {
            lock (_lock)
            {
                return [.. userIds.Where(IsUserOnline)];
            }
        }

        public int GetOnlineCount() => _userConnections.Count(kv => kv.Value.Count > 0);

        public IReadOnlyDictionary<int, int> GetConnectionCounts()
        {
            lock (_lock)
            {
                return _userConnections.ToDictionary(kv => kv.Key, kv => kv.Value.Count);
            }
        }
    }
}