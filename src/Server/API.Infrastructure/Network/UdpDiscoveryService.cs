using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace API.Infrastructure.Network;

public sealed partial class UdpDiscoveryService(ILogger<UdpDiscoveryService> logger, IConfiguration configuration) : BackgroundService
{
    private const int DiscoveryPort = 5275;
    private const int ApiPort = 5274;
    private const string RequestMagic = "MESSENGER_DISCOVER";
    private const string ResponsePrefix = "MESSENGER_HERE:";

    private static readonly NetworkInterfaceType[] PriorityInterfaceTypes =
    [
        NetworkInterfaceType.Wireless80211,
        NetworkInterfaceType.Ethernet,
        NetworkInterfaceType.GigabitEthernet
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var udp = CreateUdpClient();
        LogListenerStarted(DiscoveryPort);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(stoppingToken);
                await ProcessDiscoveryRequest(udp, result);
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

    private static UdpClient CreateUdpClient()
    {
        var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
        udp.EnableBroadcast = true;
        return udp;
    }

    private async Task ProcessDiscoveryRequest(UdpClient udp, UdpReceiveResult result)
    {
        var message = Encoding.UTF8.GetString(result.Buffer).Trim();
        if (message != RequestMagic)
            return;

        LogDiscoveryRequest(result.RemoteEndPoint);

        var responseStr = BuildResponseString(result.RemoteEndPoint.Address);
        var response = Encoding.UTF8.GetBytes(responseStr);
        await udp.SendAsync(response, response.Length, result.RemoteEndPoint);

        LogDiscoveryResponse(result.RemoteEndPoint, responseStr);
    }

    private string BuildResponseString(IPAddress requesterIp)
    {
        var responseIp = GetResponseIp(requesterIp);
        return string.IsNullOrEmpty(responseIp)
            ? $"{ResponsePrefix}{ApiPort}"
            : $"{ResponsePrefix}{ApiPort}:{responseIp}";
    }

    private string GetResponseIp(IPAddress requesterIp)
    {
        var externalIp = configuration["Discovery:ExternalIp"];
        if (!string.IsNullOrWhiteSpace(externalIp))
            return externalIp;

        if (IPAddress.IsLoopback(requesterIp))
            return "127.0.0.1";

        return FindSubnetIp(requesterIp) ?? FindFallbackIp() ?? "127.0.0.1";
    }

    private string? FindSubnetIp(IPAddress requesterIp)
    {
        try
        {
            foreach (var type in PriorityInterfaceTypes)
            {
                var ip = FindIpInSubnet(requesterIp, type);
                if (ip != null) return ip;
            }

            return FindIpInSubnet(requesterIp, null);
        }
        catch (Exception ex)
        {
            LogDiscoveryError(ex);
            return null;
        }
    }

    private static string? FindFallbackIp()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(IsValidFallbackInterface)
                .OrderByDescending(i => Array.IndexOf(PriorityInterfaceTypes, i.NetworkInterfaceType))
                .SelectMany(i => i.GetIPProperties().UnicastAddresses)
                .Where(a => IsValidUnicastAddress(a))
                .Select(a => a.Address.ToString())
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsValidFallbackInterface(NetworkInterface iface)
        => iface.OperationalStatus == OperationalStatus.Up && iface.NetworkInterfaceType != NetworkInterfaceType.Loopback
            && !IsVirtualInterface(iface.Name);

    private static bool IsVirtualInterface(string interfaceName)
        => interfaceName.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase)
            || interfaceName.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
            || interfaceName.Contains("WSL", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidUnicastAddress(UnicastIPAddressInformation addr) 
        => addr.Address.AddressFamily == AddressFamily.InterNetwork && !addr.Address.ToString().StartsWith("192.168.56.");

    private static string? FindIpInSubnet(IPAddress requesterIp, NetworkInterfaceType? filterType)
    {
        foreach (var iface in GetValidInterfaces(filterType))
        {
            var matchingIp = GetMatchingUnicastAddress(iface, requesterIp);
            if (matchingIp != null)
                return matchingIp;
        }
        return null;
    }

    private static IEnumerable<NetworkInterface> GetValidInterfaces(NetworkInterfaceType? filterType)
        => NetworkInterface.GetAllNetworkInterfaces().Where(i => i.OperationalStatus == OperationalStatus.Up
            && i.NetworkInterfaceType != NetworkInterfaceType.Loopback
            && (!filterType.HasValue || i.NetworkInterfaceType == filterType.Value)
            && !IsVirtualInterface(i.Name));

    private static string? GetMatchingUnicastAddress(NetworkInterface iface, IPAddress requesterIp)
        => iface.GetIPProperties().UnicastAddresses
        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && a.IPv4Mask != null
        && !a.Address.ToString().StartsWith("192.168.56."))
            .FirstOrDefault(a => IsInSameSubnet(a.Address, requesterIp, a.IPv4Mask))
            ?.Address.ToString();

    private static bool IsInSameSubnet(IPAddress address, IPAddress other, IPAddress mask)
    {
        var a = address.GetAddressBytes();
        var b = other.GetAddressBytes();
        var m = mask.GetAddressBytes();
        for (var i = 0; i < 4; i++)
        {
            if ((a[i] & m[i]) != (b[i] & m[i]))
                return false;
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