using Shared.Enum;

namespace Shared.Dto.Call;

/// <summary>
/// Полное состояние звонка — отправляется при реконнекте
/// или при входе в уже активный звонок
/// </summary>
public class CallStateDto
{
    public string CallId { get; set; } = string.Empty;
    public int ChatId { get; set; }
    public CallStatus Status { get; set; }
    public int InitiatorId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public bool IsGroupCall { get; set; }
    public List<CallParticipantDto> Participants { get; set; } = [];
}