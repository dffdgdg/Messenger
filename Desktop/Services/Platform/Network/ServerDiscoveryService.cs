using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Desktop.Services.Platform.Network;

public sealed class ServerDiscoveryService(ILogger<ServerDiscoveryService> logger) : IServerDiscoveryService
{
    private const int DiscoveryPort = 5275;
    private const string RequestMagic = "MESSENGER_DISCOVER";
    private const string ResponsePrefix = "MESSENGER_HERE:";

    public async Task<string?> DiscoverAsync(int timeoutMs = 3000, CancellationToken ct = default)
    {
        using var udp = new UdpClient();
        udp.EnableBroadcast = true;
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        var request = Encoding.UTF8.GetBytes(RequestMagic);

        try
        {
            await udp.SendAsync(request, request.Length,
                new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
            logger.LogInformation("[Discovery] Broadcast отправлен");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Discovery] Не удалось отправить broadcast");
            return null;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var result = await udp.ReceiveAsync(cts.Token);
                var message = Encoding.UTF8.GetString(result.Buffer).Trim();

                if (!message.StartsWith(ResponsePrefix, StringComparison.Ordinal))
                    continue;

                var payload = message[ResponsePrefix.Length..];
                var parts = payload.Split(':', 2);
                var port = parts[0];

                var ip = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])
                    ? parts[1]
                    : result.RemoteEndPoint.Address.ToString();

                var url = $"http://{ip}:{port}/";
                logger.LogInformation("[Discovery] Сервер найден: {Url}", url);
                return url;
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("[Discovery] Таймаут — сервер не найден");
        }

        return null;
    }
}