using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace API.Services;

public sealed partial class UdpDiscoveryService(ILogger<UdpDiscoveryService> logger, IConfiguration configuration) : BackgroundService
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

                var responseIp = GetResponseIp(result.RemoteEndPoint.Address);

                var responseStr = string.IsNullOrEmpty(responseIp)
                    ? $"{ResponsePrefix}{ApiPort}"
                    : $"{ResponsePrefix}{ApiPort}:{responseIp}";

                var response = Encoding.UTF8.GetBytes(responseStr);
                await udp.SendAsync(response, response.Length, result.RemoteEndPoint);

                LogDiscoveryResponse(result.RemoteEndPoint, responseStr);
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

    private string GetResponseIp(IPAddress requesterIp)
    {
        var externalIp = configuration["Discovery:ExternalIp"];
        if (!string.IsNullOrWhiteSpace(externalIp))
            return externalIp;

        try
        {
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (var addr in iface.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    if (IsInSameSubnet(addr.Address, requesterIp, addr.IPv4Mask))
                        return addr.Address.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            LogDiscoveryError(ex);
        }

        return string.Empty;
    }

    private static bool IsInSameSubnet(IPAddress address, IPAddress other, IPAddress mask)
    {
        var a = address.GetAddressBytes();
        var b = other.GetAddressBytes();
        var m = mask.GetAddressBytes();

        for (var i = 0; i < 4; i++)
        {
            if ((a[i] & m[i]) != (b[i] & m[i]))
            {
                return false;
            }
        }

        return true;
    }

    #region Log

    [LoggerMessage(Level = LogLevel.Information, Message = "[Discovery] UDP listener запущен на порту {Port}")]
    private partial void LogListenerStarted(int port);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Discovery] Запрос от {Endpoint}")]
    private partial void LogDiscoveryRequest(IPEndPoint endpoint);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Discovery] Ответ для {Endpoint}: {Response}")]
    private partial void LogDiscoveryResponse(IPEndPoint endpoint, string response);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[Discovery] Ошибка")]
    private partial void LogDiscoveryError(Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "[Discovery] UDP listener остановлен")]
    private partial void LogListenerStopped();

    #endregion
}