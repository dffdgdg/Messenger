using System.Net;
using System.Net.Sockets;
using System.Text;

namespace API.Services;

public sealed partial class UdpDiscoveryService(ILogger<UdpDiscoveryService> logger) : BackgroundService
{
    private const int DiscoveryPort = 5275;
    private const int ApiPort = 5274;
    private const string RequestMagic = "MESSENGER_DISCOVER";
    private const string ResponsePrefix = "MESSENGER_HERE:";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
        udp.EnableBroadcast = true;

        LogListenerStarted(DiscoveryPort);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(stoppingToken);
                var message = Encoding.UTF8.GetString(result.Buffer).Trim();

                if (message != RequestMagic)
                    continue;

                LogDiscoveryRequest(result.RemoteEndPoint);

                var response = Encoding.UTF8.GetBytes($"{ResponsePrefix}{ApiPort}");
                await udp.SendAsync(response, response.Length, result.RemoteEndPoint);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogDiscoveryError(ex);
            }
        }

        LogListenerStopped();
    }

    #region Log

    [LoggerMessage(Level = LogLevel.Information, Message = "[Discovery] UDP listener запущен на порту {Port}")]
    private partial void LogListenerStarted(int port);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Discovery] Запрос от {Endpoint}")]
    private partial void LogDiscoveryRequest(IPEndPoint endpoint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[Discovery] Ошибка")]
    private partial void LogDiscoveryError(Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "[Discovery] UDP listener остановлен")]
    private partial void LogListenerStopped();

    #endregion
}