namespace Shared.Dto.Call;

public class RelayEndpointInfo
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string CallId { get; set; } = string.Empty;
}