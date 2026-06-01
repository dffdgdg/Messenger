using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace API.Services.Features.Call;

public sealed class CallRelayService(
    IServiceProvider serviceProvider,
    CallMixerService mixer,
    IConfiguration configuration,
    ILogger<CallRelayService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, IPEndPoint>> _endpoints = new();
    private readonly int _port = configuration.GetValue("CallSettings:RelayPort", 5276);
    private UdpClient? _udpClient;
    private int _mixedSequence;

    public int Port => _port;

    public void RegisterCall(string callId)
    {
        _endpoints.GetOrAdd(callId, _ => new ConcurrentDictionary<int, IPEndPoint>());
        mixer.RegisterCall(callId);
    }

    public void AddParticipant(string callId, int userId)
    {
        RegisterCall(callId);
        mixer.AddParticipant(callId, userId, SendMixedAudio);
    }

    public void RemoveParticipant(string callId, int userId)
    {
        if (_endpoints.TryGetValue(callId, out var endpoints))
            endpoints.TryRemove(userId, out _);

        mixer.RemoveParticipant(callId, userId);
    }

    public void RemoveCall(string callId)
    {
        _endpoints.TryRemove(callId, out _);
        mixer.RemoveCall(callId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, _port));
        logger.LogInformation("Call relay UDP запущен на порту {Port}", _port);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = await _udpClient.ReceiveAsync(stoppingToken);
                ProcessPacket(result.Buffer, result.RemoteEndPoint);
            }
        }
        catch (OperationCanceledException)
        {
            // штатная остановка
        }
        finally
        {
            _udpClient.Dispose();
            _udpClient = null;
        }
    }

    private void ProcessPacket(byte[] packet, IPEndPoint remoteEndPoint)
    {
        if (packet.Length < 10) return;

        var callIdLength = packet[0];
        if (callIdLength <= 0 || packet.Length < 1 + callIdLength + 8) return;

        var callId = System.Text.Encoding.UTF8.GetString(packet, 1, callIdLength);
        var offset = 1 + callIdLength;
        var userId = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(offset, 4));
        offset += 4;
        _ = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(offset, 4));
        offset += 4;

        if (!IsParticipant(callId, userId)) return;

        var endpoints = _endpoints.GetOrAdd(callId, _ => new ConcurrentDictionary<int, IPEndPoint>());
        endpoints[userId] = remoteEndPoint;

        mixer.AddParticipant(callId, userId, SendMixedAudio);
        mixer.ReceiveAudio(callId, userId, packet.AsSpan(offset));
    }

    private bool IsParticipant(string callId, int userId)
    {
        var sessions = serviceProvider.GetRequiredService<ICallSessionService>();
        var session = sessions.GetCall(callId);
        return session?.ActiveParticipants.ContainsKey(userId) == true;
    }

    private void SendMixedAudio(int userId, byte[] opusData, int opusLength)
    {
        if (_udpClient == null) return;

        IPEndPoint? endpoint = null;
        foreach (var (_, endpoints) in _endpoints)
        {
            if (endpoints.TryGetValue(userId, out endpoint))
                break;
        }

        if (endpoint == null) return;

        var packet = new byte[4 + opusLength];
        BinaryPrimitives.WriteInt32LittleEndian(
            packet.AsSpan(0, 4),
            Interlocked.Increment(ref _mixedSequence));
        Buffer.BlockCopy(opusData, 0, packet, 4, opusLength);

        try
        {
            _udpClient.Send(packet, packet.Length, endpoint);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка отправки микса пользователю {UserId}", userId);
        }
    }
}