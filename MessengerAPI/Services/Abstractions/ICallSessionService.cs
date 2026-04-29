using MessengerShared.DTO.Call;

namespace MessengerAPI.Services.Abstractions;

public interface ICallSessionService
{
    /// <summary>
    /// Создать новый звонок.
    /// Возвращает null если в чате уже есть активный звонок.
    /// </summary>
    Task<CallSession?> CreateCallAsync(int chatId, int initiatorId, string initiatorConnectionId, bool isGroupCall);

    /// <summary>Получить активный звонок по callId</summary>
    CallSession? GetCall(string callId);

    /// <summary>Получить активный звонок в чате (если есть)</summary>
    CallSession? GetActiveCallInChat(int chatId);

    /// <summary>
    /// Участник принимает звонок.
    /// Возвращает false если звонок не найден или уже завершён.
    /// </summary>
    bool JoinCall(string callId, int userId, string connectionId);

    /// <summary>
    /// Участник покидает звонок.
    /// Возвращает true если после ухода звонок нужно завершить (все ушли).
    /// </summary>
    bool LeaveCall(string callId, int userId, out bool shouldEnd);

    /// <summary>Обновить ConnectionId участника (после реконнекта)</summary>
    void UpdateConnectionId(string callId, int userId, string newConnectionId);

    /// <summary>Обновить mute-статус участника</summary>
    bool SetMuted(string callId, int userId, bool isMuted);

    /// <summary>Завершить звонок и удалить из хранилища</summary>
    Task<TimeSpan> EndCallAsync(string callId);

    /// <summary>
    /// Маппинг CallSession → CallStateDto для отправки клиенту
    /// </summary>
    CallStateDto ToStateDto(CallSession session, Func<int, string?> avatarResolver, Func<int, string?> nameResolver);

    IEnumerable<CallSession> GetAllSessionsForUser(int userId);
    bool SetSpeaking(string callId, int userId, bool isSpeaking);
}