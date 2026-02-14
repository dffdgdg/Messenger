using System;

namespace MessengerDesktop.Infrastructure.Configuration;

/// <summary>
/// Все API endpoints приложения.
/// Структура соответствует контроллерам на сервере.
/// </summary>
public static class ApiEndpoints
{
    private const string Api = "api";

    /// <summary>
    /// AuthController - авторизация
    /// </summary>
    public static class Auth
    {
        private const string Base = $"{Api}/auth";
        /// <summary>POST api/auth/login</summary>
        public const string Login = $"{Base}/login";
        /// <summary>POST api/auth/validate</summary>
        public const string Validate = $"{Base}/validate";
        /// <summary>POST api/auth/logout</summary>
        public const string Logout = $"{Base}/logout";
    }

    /// <summary>
    /// UserController - пользователи
    /// </summary>
    public static class User
    {
        private const string Base = $"{Api}/user";

        /// <summary>GET api/user</summary>
        public const string GetAll = Base;

        /// <summary>GET api/user/online</summary>
        public const string Online = $"{Base}/online";

        /// <summary>POST api/user/status/batch</summary>
        public const string StatusBatch = $"{Base}/status/batch";

        /// <summary>GET/PUT/DELETE api/user/{id}</summary>
        public static string ById(int id) => $"{Base}/{id}";

        /// <summary>GET api/user/{id}/status</summary>
        public static string Status(int id) => $"{Base}/{id}/status";

        /// <summary>GET/PUT api/user/{id}/avatar</summary>
        public static string Avatar(int id) => $"{Base}/{id}/avatar";

        /// <summary>PUT api/user/{id}/username</summary>
        public static string Username(int id) => $"{Base}/{id}/username";

        /// <summary>PUT api/user/{id}/password</summary>
        public static string Password(int id) => $"{Base}/{id}/password";
    }

    /// <summary>
    /// ChatsController - чаты
    /// </summary>
    public static class Chat
    {
        private const string Base = $"{Api}/chats";

        /// <summary>POST api/chats</summary>
        public const string Create = Base;

        /// <summary>GET/PUT/DELETE api/chats/{id}</summary>
        public static string ById(int id) => $"{Base}/{id}";

        /// <summary>GET/POST api/chats/{chatId}/members</summary>
        public static string Members(int chatId) => $"{Base}/{chatId}/members";

        /// <summary>DELETE api/chats/{chatId}/members/{userId}</summary>
        public static string RemoveMember(int chatId, int userId) => $"{Base}/{chatId}/members/{userId}";

        /// <summary>POST api/chats/{chatId}/leave?userId={userId}</summary>
        public static string Leave(int chatId, int userId) => $"{Base}/{chatId}/leave?userId={userId}";

        /// <summary>GET/PUT api/chats/{chatId}/avatar</summary>
        public static string Avatar(int chatId) => $"{Base}/{chatId}/avatar";

        /// <summary>GET api/chats/user/{userId}</summary>
        public static string UserChats(int userId) => $"{Base}/user/{userId}";

        /// <summary>GET api/chats/user/{userId}/dialogs</summary>
        public static string UserDialogs(int userId) => $"{Base}/user/{userId}/dialogs";

        /// <summary>GET api/chats/user/{userId}/groups</summary>
        public static string UserGroups(int userId) => $"{Base}/user/{userId}/groups";

        /// <summary>GET api/chats/user/{userId}/contact/{contactUserId}</summary>
        public static string UserContact(int userId, int contactUserId) => $"{Base}/user/{userId}/contact/{contactUserId}";
    }

    /// <summary>
    /// MessagesController - сообщения
    /// </summary>
    public static class Message
    {
        private const string Base = $"{Api}/messages";

        /// <summary>POST api/messages</summary>
        public const string Create = Base;

        /// <summary>GET/PUT/DELETE api/messages/{id}</summary>
        public static string ById(int id) => $"{Base}/{id}";

        /// <summary>GET api/messages/{id}/transcription</summary>
        public static string Transcription(int id) => $"{Base}/{id}/transcription";

        /// <summary>POST api/messages/{id}/transcription/retry</summary>
        public static string TranscriptionRetry(int id) => $"{Base}/{id}/transcription/retry";

        /// <summary>GET api/messages/chat/{chatId}?userId={userId}&amp;page={page}&amp;pageSize={pageSize}</summary>
        public static string ForChat(int chatId, int userId, int page, int pageSize)
            => $"{Base}/chat/{chatId}?userId={userId}&page={page}&pageSize={pageSize}";

        /// <summary>GET api/messages/chat/{chatId}/around/{messageId}?userId={userId}&amp;count={count}</summary>
        public static string Around(int chatId, int messageId, int userId, int count)
            => $"{Base}/chat/{chatId}/around/{messageId}?userId={userId}&count={count}";

        /// <summary>GET api/messages/chat/{chatId}/before/{beforeId}?userId={userId}&amp;count={count}</summary>
        public static string Before(int chatId, int beforeId, int userId, int count)
            => $"{Base}/chat/{chatId}/before/{beforeId}?userId={userId}&count={count}";

        /// <summary>GET api/messages/chat/{chatId}/after/{afterId}?userId={userId}&amp;count={count}</summary>
        public static string After(int chatId, int afterId, int userId, int count)
            => $"{Base}/chat/{chatId}/after/{afterId}?userId={userId}&count={count}";

        /// <summary>GET api/messages/user/{userId}/search?query={query}&amp;page={page}&amp;pageSize={pageSize}</summary>
        public static string Search(int userId, string query, int page, int pageSize)
            => $"{Base}/user/{userId}/search?query={Uri.EscapeDataString(query)}&page={page}&pageSize={pageSize}";
    }

    /// <summary>
    /// FilesController - файлы
    /// </summary>
    public static class File
    {
        private const string Base = $"{Api}/files";

        /// <summary>POST api/files/upload?chatId={chatId}</summary>
        public static string Upload(int chatId) => $"{Base}/upload?chatId={chatId}";
    }

    /// <summary>
    /// PollController - опросы
    /// </summary>
    public static class Poll
    {
        private const string Base = $"{Api}/poll";

        /// <summary>POST api/poll</summary>
        public const string Create = Base;

        /// <summary>POST api/poll/vote</summary>
        public const string Vote = $"{Base}/vote";

        /// <summary>GET api/poll/{pollId}?userId={userId}</summary>
        public static string ById(int pollId, int userId) => $"{Base}/{pollId}?userId={userId}";
    }

    /// <summary>
    /// DepartmentController - отделы
    /// </summary>
    public static class Department
    {
        private const string Base = $"{Api}/department";

        /// <summary>GET api/department</summary>
        public const string GetAll = Base;

        /// <summary>POST api/department</summary>
        public const string Create = Base;

        /// <summary>GET/PUT/DELETE api/department/{id}</summary>
        public static string ById(int id) => $"{Base}/{id}";

        /// <summary>GET/POST api/department/{id}/members</summary>
        public static string Members(int id) => $"{Base}/{id}/members";

        /// <summary>DELETE api/department/{departmentId}/members/{userId}</summary>
        public static string RemoveMember(int departmentId, int userId) => $"{Base}/{departmentId}/members/{userId}";

        /// <summary>GET api/department/{id}/can-manage</summary>
        public static string CanManage(int id) => $"{Base}/{id}/can-manage";
    }

    /// <summary>
    /// NotificationController - уведомления
    /// </summary>
    public static class Notification
    {
        private const string Base = $"{Api}/notification";

        /// <summary>GET api/notification/settings</summary>
        public const string AllSettings = $"{Base}/settings";

        /// <summary>POST api/notification/chat/mute</summary>
        public const string SetMute = $"{Base}/chat/mute";

        /// <summary>GET api/notification/chat/{chatId}/settings</summary>
        public static string ChatSettings(int chatId) => $"{Base}/chat/{chatId}/settings";
    }

    /// <summary>
    /// ReadReceiptsController - прочтения
    /// </summary>
    public static class ReadReceipt
    {
        private const string Base = $"{Api}/readreceipts";

        /// <summary>POST api/readreceipts/mark-read</summary>
        public const string MarkRead = $"{Base}/mark-read";

        /// <summary>GET api/readreceipts/unread-counts</summary>
        public const string AllUnreadCounts = $"{Base}/unread-counts";

        /// <summary>GET api/readreceipts/chat/{chatId}/unread-count</summary>
        public static string UnreadCount(int chatId) => $"{Base}/chat/{chatId}/unread-count";
    }

    /// <summary>
    /// AdminController - администрирование (только для Admin)
    /// </summary>
    public static class Admin
    {
        private const string Base = $"{Api}/admin";

        /// <summary>GET api/admin/users</summary>
        public const string Users = $"{Base}/users";

        /// <summary>POST api/admin/users/{userId}/toggle-ban</summary>
        public static string ToggleBan(int userId) => $"{Base}/users/{userId}/toggle-ban";
    }
}