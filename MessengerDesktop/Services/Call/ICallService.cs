using System;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.Call;

public interface ICallService : IAsyncDisposable
{
    bool IsInCall { get; }
    bool IsMuted { get; }
    string? ActiveCallId { get; }
    int? ActiveChatId { get; }

    /// <summary>Инициировать исходящий звонок</summary>
    Task StartCallAsync(int chatId);

    /// <summary>Принять входящий / присоединиться к активному</summary>
    Task JoinCallAsync(string callId, int chatId);

    /// <summary>Покинуть текущий звонок</summary>
    Task LeaveCallAsync();

    /// <summary>Отклонить входящий (только Contact)</summary>
    Task DeclineCallAsync(string callId);

    /// <summary>Отменить исходящий до принятия (только Contact)</summary>
    Task CancelCallAsync();

    /// <summary>Переключить микрофон</summary>
    Task ToggleMuteAsync();

    event Action<bool>? MuteChanged;
    event Action? CallStarted;
    event Action? CallEnded;
}