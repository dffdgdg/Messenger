using Core.Services.Platform.Abstractions;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Core.Services.Platform.Network;

public sealed partial class ServerDiscoveryService(ILogger<ServerDiscoveryService> logger)
    : IServerDiscoveryService
{
    private const int DiscoveryPort = 5275;
    private const string RequestMagic = "MESSENGER_DISCOVER";
    private const string ResponsePrefix = "MESSENGER_HERE:";
    private const string LocalIp = "127.0.0.1";

    public async Task<string?> DiscoverAsync(
        int timeoutMs = 3000,
        CancellationToken ct = default)
    {
        string? broadcastResult = null;

        try
        {
            broadcastResult = await TryBroadcastAsync(timeoutMs, ct);
        }
        catch (Exception ex)
        {
            LogBroadcastError(ex);
        }

        string? localResult = null;

        try
        {
            localResult = await TryDirectAsync(LocalIp, 1000, ct);
        }
        catch (Exception ex)
        {
            LogDirectError(ex, LocalIp);
        }

        if (localResult != null)
        {
            LogLocalServerUsed();
            return localResult;
        }

        if (broadcastResult != null)
            return broadcastResult;

        var envIp = Environment.GetEnvironmentVariable("MESSENGER_SERVER_IP");

        if (!string.IsNullOrWhiteSpace(envIp) && envIp != LocalIp)
        {
            try
            {
                var result = await TryDirectAsync(envIp, 1500, ct);

                if (result != null)
                    return result;
            }
            catch (Exception ex)
            {
                LogDirectError(ex, envIp);
            }
        }

        LogFallback();

        return "http://localhost:5274/";
    }

    private async Task<string?> TryBroadcastAsync(int timeoutMs, CancellationToken ct)
    {
        using var udp = new UdpClient();

        udp.EnableBroadcast = true;
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        var request = Encoding.UTF8.GetBytes(RequestMagic);

        await udp.SendAsync(request, request.Length,
            new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));

        LogBroadcastSent();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        return await ReceiveResponseAsync(udp, cts.Token);
    }

    private async Task<string?> TryDirectAsync(string ip, int timeoutMs, CancellationToken ct)
    {
        using var udp = new UdpClient();

        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        LogDirectSent(ip);

        var request = Encoding.UTF8.GetBytes(RequestMagic);

        await udp.SendAsync(request, request.Length,
            new IPEndPoint(IPAddress.Parse(ip), DiscoveryPort));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        return await ReceiveResponseAsync(udp, cts.Token);
    }

    private async Task<string?> ReceiveResponseAsync(UdpClient udp, CancellationToken ct)
    {
        try
        {
            var result = await udp.ReceiveAsync(ct);

            var message = Encoding.UTF8.GetString(result.Buffer).Trim();

            LogReceived(message, result.RemoteEndPoint.ToString());

            if (!message.StartsWith(ResponsePrefix, StringComparison.Ordinal))
                return null;

            var payload = message[ResponsePrefix.Length..];
            var parts = payload.Split(':', 2);
            var port = parts[0];

            var serverIp = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])
                ? parts[1]
                : LocalIp;

            var isLocalRequest = IPAddress.IsLoopback(result.RemoteEndPoint.Address);
            var ip = isLocalRequest ? LocalIp : serverIp;

            LogServerFound(ip, port);
            return $"http://{ip}:{port}/";
        }
        catch (OperationCanceledException)
        {
            LogDiscoveryTimeout();
            return null;
        }
    }

    #region Logging

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "[Discovery] Broadcast отправлен")]
    private partial void LogBroadcastSent();

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "[Discovery] Прямой запрос на {Ip}")]
    private partial void LogDirectSent(string ip);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "[Discovery] Получено: {Message} от {Endpoint}")]
    private partial void LogReceived(string message, string endpoint);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "[Discovery] Сервер найден: http://{Ip}:{Port}/")]
    private partial void LogServerFound(string ip, string port);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "[Discovery] Таймаут")]
    private partial void LogDiscoveryTimeout();

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "[Discovery] Fallback на http://localhost:5274/")]
    private partial void LogFallback();

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "[Discovery] Локальный сервер доступен, используем 127.0.0.1")]
    private partial void LogLocalServerUsed();

    [LoggerMessage(EventId = 8, Level = LogLevel.Debug, Message = "[Discovery] Broadcast error")]
    private partial void LogBroadcastError(Exception ex);

    [LoggerMessage(EventId = 9, Level = LogLevel.Debug, Message = "[Discovery] Direct request error for {Ip}")]
    private partial void LogDirectError(Exception ex, string ip);

    #endregion
}