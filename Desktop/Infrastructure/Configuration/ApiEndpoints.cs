using System.Text;

namespace Desktop.Infrastructure.Configuration;

public static class ApiEndpoints
{
    private const string Api = "api";

    public static class Auth
    {
        private const string Base = $"{Api}/auth";
        public const string Login = $"{Base}/login";
        public const string Refresh = $"{Base}/refresh";
        public const string Revoke = $"{Base}/revoke";
    }

    public static class Users
    {
        private const string Base = $"{Api}/users";

        public const string GetAll = Base;
        public const string Online = $"{Base}/online";
        public const string StatusBatch = $"{Base}/status/batch";

        public static string ById(int id) => $"{Base}/{id}";
        public static string Status(int id) => $"{Base}/{id}/status";
        public static string Avatar(int id) => $"{Base}/{id}/avatar";
        public static string Username(int id) => $"{Base}/{id}/username";
        public static string Password(int id) => $"{Base}/{id}/password";
    }

    public static class Chats
    {
        private const string Base = $"{Api}/chats";

        public const string Create = Base;

        public static string ById(int id) => $"{Base}/{id}";
        public static string Pin(int id) => $"{Base}/{id}/pin";
        public static string PinnedForChat(int chatId) => $"{Base}/chat/{chatId}/pinned";
        public static string Members(int chatId) => $"{Base}/{chatId}/members";
        public static string RemoveMember(int chatId, int userId) => $"{Base}/{chatId}/members/{userId}";
        public static string MembersDetailed(int chatId) => $"{Base}/{chatId}/members/detailed";
        public static string MemberRole(int chatId, int userId, ChatRole role) => $"{Base}/{chatId}/members/{userId}/role?role={role}";
        public static string Leave(int chatId, int userId) => RemoveMember(chatId, userId);
        public static string Avatar(int chatId) => $"{Base}/{chatId}/avatar";
        public static string UserChats(int userId) => $"{Base}/user/{userId}";
        public static string UserDialogs(int userId) => $"{Base}/user/{userId}/dialogs";
        public static string UserGroups(int userId) => $"{Base}/user/{userId}/groups";
        public static string UserContact(int userId, int contactUserId) => $"{Base}/user/{userId}/contact/{contactUserId}";
    }

    public static class Messages
    {
        private const string Base = $"{Api}/messages";

        public const string Create = Base;

        public static string ById(int id) => $"{Base}/{id}";
        public static string Pin(int id) => $"{Base}/{id}/pin";
        public static string PinnedForChat(int chatId) => $"{Base}/chat/{chatId}/pinned";
        public static string Counts(int chatId) => $"{Base}/chat/{chatId}/counts";

        public static string Latest(int chatId, int take)
            => $"{Base}/chat/{chatId}/latest?take={take}";

        public static string Around(int chatId, int messageId, int userId, int count)
            => $"{Base}/chat/{chatId}/around/{messageId}?userId={userId}&count={count}";

        public static string Before(int chatId, int beforeId, int userId, int count)
            => $"{Base}/chat/{chatId}/before/{beforeId}?userId={userId}&count={count}";

        public static string After(int chatId, int afterId, int userId, int count)
            => $"{Base}/chat/{chatId}/after/{afterId}?userId={userId}&count={count}";

        public static string Search(int userId, GlobalSearchQueryDto dto)
        {
            var sb = new StringBuilder($"{Base}/user/{userId}/search?query={Uri.EscapeDataString(dto.Query ?? string.Empty)}&page={dto.Page}&pageSize={dto.PageSize}");

            AppendSearchParameters(sb, dto);

            if (dto.FilterChatId.HasValue)
                sb.Append($"&filterChatId={dto.FilterChatId.Value}");

            return sb.ToString();
        }

        public static string Search(int userId) => $"{Base}/user/{userId}/search";
        public static string ChatSearch(int chatId) => $"{Base}/chat/{chatId}/search";

        public static string ChatSearch(int chatId, SearchMessagesQueryDto dto)
        {
            var sb = new StringBuilder($"{Base}/chat/{chatId}/search?query={Uri.EscapeDataString(dto.Query ?? string.Empty)}&page={dto.Page}&pageSize={dto.PageSize}");

            AppendSearchParameters(sb, dto);

            return sb.ToString();
        }

        private static void AppendSearchParameters(StringBuilder sb, SearchMessagesQueryDto dto)
        {
            if (dto.SenderId.HasValue)
                sb.Append($"&senderId={dto.SenderId.Value}");
            if (dto.HasFiles.HasValue)
                sb.Append($"&hasFiles={dto.HasFiles.Value.ToString().ToLowerInvariant()}");
            if (dto.HasVoice.HasValue)
                sb.Append($"&hasVoice={dto.HasVoice.Value.ToString().ToLowerInvariant()}");
            if (dto.HasPoll.HasValue)
                sb.Append($"&hasPoll={dto.HasPoll.Value.ToString().ToLowerInvariant()}");
            if (dto.OnlyText.HasValue)
                sb.Append($"&onlyText={dto.OnlyText.Value.ToString().ToLowerInvariant()}");
            if (dto.DateFrom.HasValue)
                sb.Append($"&dateFrom={dto.DateFrom.Value:yyyy-MM-dd}");
            if (dto.DateTo.HasValue)
                sb.Append($"&dateTo={dto.DateTo.Value:yyyy-MM-dd}");
            if (dto.OldestFirst)
                sb.Append("&oldestFirst=true");
        }
    }

    public static class Files
    {
        private const string Base = $"{Api}/files";
        public static string Upload(int chatId) => $"{Base}/upload?chatId={chatId}";
    }

    public static class Polls
    {
        private const string Base = $"{Api}/polls";
        public const string Vote = $"{Base}/vote";
        public const string Create = Base;
        public static string ById(int pollId, int userId) => $"{Base}/{pollId}?userId={userId}";
        public static string Close(int pollId) => $"{Base}/{pollId}/close";
    }

    public static class Departments
    {
        private const string Base = $"{Api}/departments";
        public const string GetAll = Base;
        public const string Create = Base;
        public static string ById(int id) => $"{Base}/{id}";
        public static string Members(int id) => $"{Base}/{id}/members";
        public static string RemoveMember(int departmentId, int userId) => $"{Base}/{departmentId}/members/{userId}";
        public static string CanManage(int id) => $"{Base}/{id}/can-manage";
    }

    public static class Notifications
    {
        private const string Base = $"{Api}/notifications";
        public const string AllSettings = $"{Base}/settings";
        public const string SetMute = $"{Base}/chat/mute";
        public static string ChatSettings(int chatId) => $"{Base}/chat/{chatId}/settings";
    }

    public static class ReadReceipts
    {
        private const string Base = $"{Api}/readreceipts";
        public const string MarkRead = $"{Base}/mark-read";
        public const string AllUnreadCounts = $"{Base}/unread-counts";
        public static string UnreadCount(int chatId) => $"{Base}/chat/{chatId}/unread-count";
    }

    public static class Admin
    {
        private const string Base = $"{Api}/admin";
        public const string AllUsers = $"{Base}/users";
        public static string UserById(int userId) => $"{Base}/users/{userId}";
        public static string ToggleBan(int userId) => $"{Base}/users/{userId}/toggle-ban";
        public static string ResetPassword(int userId) => $"{Base}/users/{userId}/reset-password";
    }
}