using API.Application.Services.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace API.Infrastructure.Services.Features.Call;

public sealed class CallRelayService : BackgroundService
{
    private const int SioUdpConnectionReset = -1744830452;

    private readonly IServiceProvider _serviceProvider;
    private readonly CallMixerService _mixer;
    private readonly ILogger<CallRelayService> _logger;

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, IPEndPoint>> _endpoints = new();
    private readonly ConcurrentDictionary<int, IPEndPoint> _userEndpoints = new();

    // Lazy разрывает циклическую зависимость:
    // CallRelayService → ICallSessionService → CallSessionService → CallRelayService
    // Singleton резолвится один раз при первом обращении, не в конструкторе.
    private readonly Lazy<ICallSessionService> _callSessions;

    private readonly int _port;
    private UdpClient? _udpClient;
    private int _mixedSequence;

    public int Port => _port;

    public CallRelayService(
        IServiceProvider serviceProvider,
        CallMixerService mixer,
        IConfiguration configuration,
        ILogger<CallRelayService> logger)
    {
        _serviceProvider = serviceProvider;
        _mixer = mixer;
        _logger = logger;
        _port = configuration.GetValue("CallSettings:RelayPort", 5276);
        _callSessions = new Lazy<ICallSessionService>(
            () => _serviceProvider.GetRequiredService<ICallSessionService>());
    }

    public void RegisterCall(string callId)
    {
        _endpoints.GetOrAdd(callId, _ => new ConcurrentDictionary<int, IPEndPoint>());
        _mixer.RegisterCall(callId);
    }

    public void AddParticipant(string callId, int userId)
    {
        RegisterCall(callId);
        _mixer.AddParticipant(callId, userId, SendMixedAudio);
    }

    public void RemoveParticipant(string callId, int userId)
    {
        if (_endpoints.TryGetValue(callId, out var endpoints))
            endpoints.TryRemove(userId, out _);
        _userEndpoints.TryRemove(userId, out _);
        _mixer.RemoveParticipant(callId, userId);
    }

    public void RemoveCall(string callId)
    {
        if (_endpoints.TryRemove(callId, out var endpoints))
            foreach (var userId in endpoints.Keys)
                _userEndpoints.TryRemove(userId, out _);
        _mixer.RemoveCall(callId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, _port));
        DisableUdpConnectionReset(_udpClient);
        _logger.LogInformation("Call relay UDP запущен на порту {Port}", _port);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync(stoppingToken);
                    ProcessPacket(result.Buffer, result.RemoteEndPoint);
                }
                catch (SocketException ex)
                    when (ex.SocketErrorCode == SocketError.ConnectionReset
                          && !stoppingToken.IsCancellationRequested)
                {
                    _logger.LogDebug(ex,
                        "UDP relay получил ICMP connection reset от клиента и продолжает работу");
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _udpClient.Dispose();
            _udpClient = null;
        }
    }

    private static void DisableUdpConnectionReset(UdpClient udpClient)
    {
        if (!OperatingSystem.IsWindows()) return;
        try { udpClient.Client.IOControl((IOControlCode)SioUdpConnectionReset, [0], null); }
        catch (SocketException) { }
    }

    private void ProcessPacket(byte[] packet, IPEndPoint remoteEndPoint)
    {
        if (packet.Length < 10) return;

        var callIdLength = packet[0];
        if (callIdLength <= 0 || packet.Length < 1 + callIdLength + 8) return;

        var callId = Encoding.UTF8.GetString(packet, 1, callIdLength);
        var offset = 1 + callIdLength;
        var userId = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(offset, 4));
        offset += 4;
        offset += 4; // seq — пропускаем

        if (!IsParticipant(callId, userId)) return;

        var endpoints = _endpoints.GetOrAdd(callId, _ => new ConcurrentDictionary<int, IPEndPoint>());
        endpoints[userId] = remoteEndPoint;
        _userEndpoints[userId] = remoteEndPoint;

        _mixer.AddParticipant(callId, userId, SendMixedAudio);
        _mixer.ReceiveAudio(callId, userId, packet.AsSpan(offset));
    }

    /// <summary>
    /// O(1) — Singleton резолвится один раз через Lazy,
    /// затем только чтение из ConcurrentDictionary.
    /// </summary>
    private bool IsParticipant(string callId, int userId)
    {
        var session = _callSessions.Value.GetCall(callId);
        return session?.ActiveParticipants.ContainsKey(userId) == true;
    }

    private void SendMixedAudio(int userId, byte[] opusData, int opusLength)
    {
        if (_udpClient == null) return;
        if (!_userEndpoints.TryGetValue(userId, out var endpoint)) return;

        var packet = new byte[4 + opusLength];
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(0, 4), Interlocked.Increment(ref _mixedSequence));
        Buffer.BlockCopy(opusData, 0, packet, 4, opusLength);

        try
        {
            _udpClient.Send(packet, packet.Length, endpoint);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка отправки микса пользователю {UserId}", userId);
        }
    }
}