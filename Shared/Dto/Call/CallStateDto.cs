using Shared.Enum;

namespace Shared.Dto.Call;

public class CallStateDto
{
    public string CallId { get; set; } = string.Empty;
    public int ChatId { get; set; }
    public CallStatus Status { get; set; }
    public int InitiatorId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public bool IsGroupCall { get; set; }
    public CallMode Mode { get; set; } = CallMode.PeerToPeer;
    public List<CallParticipantDto> Participants { get; set; } = [];

    /// <summary>
    /// Сколько секунд звонок уже идёт на момент формирования ответа сервером.
    /// Клиент стартует таймер от этого значения.
    /// </summary>
    public int ElapsedSeconds { get; set; }
}