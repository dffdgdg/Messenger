using global::API.Services.Abstractions;
using global::API.Services.Features.Call;
using global::Shared.Enum;
using Microsoft.Extensions.Logging;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Messenger.LoadTests.VoiceCalls;

public sealed class VoiceCallLoadTestRunner(
    VoiceCallLoadTestOptions options,
    ICallSessionService callSessions,
    ILogger<VoiceCallLoadTestRunner> logger)
{
    private long _sentPackets;
    private long _receivedMixedPackets;
    private long _sendErrors;

    public async Task<VoiceCallLoadTestResult> RunAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();

        logger.LogWarning(
            "Запуск нагрузочного теста голосовых звонков: calls={Calls}, participantsPerCall={Participants}, " +
            "duration={Duration}s, fps={Fps}, relay={RelayHost}:{RelayPort}",
            options.Calls,
            options.ParticipantsPerCall,
            options.DurationSeconds,
            options.FramesPerSecond,
            options.RelayHost,
            options.RelayPort);

        var calls = await CreateCallsAsync();
        var clients = CreateClients(calls);
        var failedJoins = JoinParticipants(calls, clients);

        if (options.WarmUpSeconds > 0)
            await WarmUpAsync(clients, cancellationToken);

        Interlocked.Exchange(ref _sentPackets, 0);
        Interlocked.Exchange(ref _receivedMixedPackets, 0);
        Interlocked.Exchange(ref _sendErrors, 0);

        var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var stopwatch = Stopwatch.StartNew();

        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        runCts.CancelAfter(options.Duration);

        var receiveTasks = clients
            .Select(client => client.ReceiveMixedAudioAsync(runCts.Token))
            .ToArray();

        await Task.WhenAll(clients.Select(c => c.ReceiveReady));

        var sendTasks = clients
            .Select((client, index) => client.SendAudioAsync(GetRampDelay(index), runCts.Token))
            .ToArray();

        try
        {
            await Task.WhenAll(sendTasks);
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested) { }

        using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await Task.WhenAll(receiveTasks).WaitAsync(cleanupCts.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Receive-loop не завершился за 2 секунды после остановки теста.");
        }
        finally
        {
            foreach (var client in clients)
                client.Dispose();
        }

        foreach (var call in calls)
            await callSessions.EndCallAsync(call.CallId);

        stopwatch.Stop();
        process.Refresh();

        return new VoiceCallLoadTestResult(
            options,
            stopwatch.Elapsed,
            Interlocked.Read(ref _sentPackets),
            Interlocked.Read(ref _receivedMixedPackets),
            Interlocked.Read(ref _sendErrors),
            calls.Count,
            clients.Count,
            failedJoins,
            (process.TotalProcessorTime - cpuBefore).TotalSeconds);
    }

    private async Task WarmUpAsync(List<VirtualVoiceParticipant> clients, CancellationToken cancellationToken)
    {
        logger.LogWarning("Прогрев {WarmUpMs}ms...", options.WarmUpSeconds * 1000);

        // Прогреваем ThreadPool заранее
        ThreadPool.GetMinThreads(out var workerMin, out var ioMin);
        var needed = Math.Max(workerMin, clients.Count + 4);
        ThreadPool.SetMinThreads(needed, ioMin);

        using var warmupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        warmupCts.CancelAfter(options.WarmUp);

        var receiveWarmup = clients
            .Select(c => c.ReceiveWarmupAsync(warmupCts.Token))
            .ToArray();

        await Task.WhenAll(clients.Select(c => c.ReceiveReady));

        var sendWarmup = clients
            .Select(c => c.SendAudioAsync(TimeSpan.Zero, warmupCts.Token))
            .ToArray();

        try
        {
            await Task.WhenAll(sendWarmup);
        }
        catch (OperationCanceledException) { }

        try
        {
            await Task.WhenAll(receiveWarmup)
                .WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
        }
        catch (OperationCanceledException) { }

        foreach (var client in clients)
            client.ResetReceiveReady();

        logger.LogWarning("Прогрев завершён.");
    }

    private void ValidateOptions()
    {
        if (options.FramesPerSecond > 100)
            logger.LogWarning("fps={Fps} очень высокий — сеть или CPU могут стать узким местом.", options.FramesPerSecond);

        if (options.TotalParticipants > 500)
            logger.LogWarning("Создаётся {N} UDP-сокетов — убедитесь, что лимит открытых дескрипторов ОС достаточен.", options.TotalParticipants);
    }

    private async Task<List<CallDescriptor>> CreateCallsAsync()
    {
        var calls = new List<CallDescriptor>(options.Calls);
        var nextUserId = 1;

        for (var callIndex = 0; callIndex < options.Calls; callIndex++)
        {
            var chatId = 10_000 + callIndex;
            var initiatorId = nextUserId++;

            var session = await callSessions.CreateCallAsync(chatId, initiatorId, ConnectionId(initiatorId), ChatType.Chat)
                ?? throw new InvalidOperationException($"Не удалось создать звонок для chatId={chatId}.");
            var users = new List<int>(options.ParticipantsPerCall) { initiatorId };
            for (var p = 1; p < options.ParticipantsPerCall; p++)
                users.Add(nextUserId++);

            calls.Add(new CallDescriptor(session.CallId, users));
        }

        return calls;
    }

    private List<VirtualVoiceParticipant> CreateClients(List<CallDescriptor> calls)
    {
        var relayAddress = Dns.GetHostAddresses(options.RelayHost)
            .First(a => a.AddressFamily == AddressFamily.InterNetwork);
        var relayEndpoint = new IPEndPoint(relayAddress, options.RelayPort);

        return [.. calls.SelectMany(call => call.UserIds.Select(userId =>
            new VirtualVoiceParticipant(
                call.CallId,
                userId,
                relayEndpoint,
                options.FramesPerSecond,
                IncrementSentPackets,
                IncrementReceivedMixedPackets,
                IncrementSendErrors)))];
    }

    private int JoinParticipants(List<CallDescriptor> calls, List<VirtualVoiceParticipant> clients)
    {
        var failed = 0;
        var initiators = calls.ToDictionary(call => call.CallId, call => call.UserIds[0]);

        foreach (var client in clients)
        {
            if (initiators[client.CallId] == client.UserId)
                continue;

            if (!callSessions.JoinCall(client.CallId, client.UserId, ConnectionId(client.UserId)))
                failed++;
        }

        return failed;
    }

    private TimeSpan GetRampDelay(int participantIndex)
    {
        if (options.RampUpSeconds == 0 || options.TotalParticipants <= 1)
            return TimeSpan.Zero;

        var ms = options.RampUp.TotalMilliseconds * participantIndex / (options.TotalParticipants - 1);
        return TimeSpan.FromMilliseconds(ms);
    }

    private static string ConnectionId(int userId) => $"load-test-{userId}";

    private void IncrementSentPackets() => Interlocked.Increment(ref _sentPackets);
    private void IncrementReceivedMixedPackets() => Interlocked.Increment(ref _receivedMixedPackets);
    private void IncrementSendErrors() => Interlocked.Increment(ref _sendErrors);

    private sealed record CallDescriptor(string CallId, List<int> UserIds);

    private sealed class VirtualVoiceParticipant : IDisposable
    {
        private const int SioUdpConnectionReset = -1744830452;
        private const int SocketBufferBytes = 4 * 1024 * 1024;

        private readonly IPEndPoint _relayEndpoint;
        private readonly int _framesPerSecond;
        private readonly Action _onSent;
        private readonly Action _onReceived;
        private readonly Action _onSendError;
        private readonly byte[] _sendBuffer;
        private readonly int _seqOffset;
        private readonly UdpClient _client = new(new IPEndPoint(IPAddress.Loopback, 0));

        private TaskCompletionSource _receiveReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _sequence;

        public VirtualVoiceParticipant(
            string callId,
            int userId,
            IPEndPoint relayEndpoint,
            int framesPerSecond,
            Action onSent,
            Action onReceived,
            Action onSendError)
        {
            CallId = callId;
            UserId = userId;
            _relayEndpoint = relayEndpoint;
            _framesPerSecond = framesPerSecond;
            _onSent = onSent;
            _onReceived = onReceived;
            _onSendError = onSendError;

            ConfigureSocket(_client);

            var callIdBytes = Encoding.UTF8.GetBytes(callId);
            if (callIdBytes.Length > byte.MaxValue)
                throw new ArgumentException("callId не должен превышать 255 байт.", nameof(callId));

            var opusFrame = CreateVoiceFrame(userId);
            _seqOffset = 1 + callIdBytes.Length + 4;
            _sendBuffer = new byte[1 + callIdBytes.Length + 4 + 4 + opusFrame.Length];

            _sendBuffer[0] = (byte)callIdBytes.Length;
            Buffer.BlockCopy(callIdBytes, 0, _sendBuffer, 1, callIdBytes.Length);
            BinaryPrimitives.WriteInt32LittleEndian(
                _sendBuffer.AsSpan(1 + callIdBytes.Length, 4), userId);
            Buffer.BlockCopy(opusFrame, 0, _sendBuffer, _seqOffset + 4, opusFrame.Length);
        }

        public string CallId { get; }
        public int UserId { get; }
        public Task ReceiveReady => _receiveReady.Task;

        public void ResetReceiveReady()
        {
            _receiveReady = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async Task SendAudioAsync(TimeSpan initialDelay, CancellationToken cancellationToken)
        {
            if (initialDelay > TimeSpan.Zero)
                await Task.Delay(initialDelay, cancellationToken);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1d / _framesPerSecond));
            while (!cancellationToken.IsCancellationRequested)
            {
                await SendOnePacketAsync(cancellationToken);
                await timer.WaitForNextTickAsync(cancellationToken);
            }
        }

        public async Task ReceiveWarmupAsync(CancellationToken cancellationToken)
        {
            _receiveReady.TrySetResult();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await _client.ReceiveAsync(cancellationToken);
                    // намеренно не вызываем _onReceived
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException ex)
                    when (ex.SocketErrorCode == SocketError.ConnectionReset
                          && !cancellationToken.IsCancellationRequested)
                {
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        public async Task ReceiveMixedAudioAsync(CancellationToken cancellationToken)
        {
            _receiveReady.TrySetResult();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var result = await _client.ReceiveAsync(cancellationToken);
                    if (result.Buffer.Length > 4)
                        _onReceived();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException ex)
                    when (ex.SocketErrorCode == SocketError.ConnectionReset
                          && !cancellationToken.IsCancellationRequested)
                {
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        private async Task SendOnePacketAsync(CancellationToken cancellationToken)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                _sendBuffer.AsSpan(_seqOffset, 4),
                Interlocked.Increment(ref _sequence));
            try
            {
                await _client.SendAsync(_sendBuffer, _relayEndpoint, cancellationToken);
                _onSent();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                _onSendError();
            }
        }

        private static void ConfigureSocket(UdpClient client)
        {
            client.Client.ReceiveBufferSize = SocketBufferBytes;
            client.Client.SendBufferSize = SocketBufferBytes;

            if (!OperatingSystem.IsWindows()) return;
            try
            {
                client.Client.IOControl((IOControlCode)SioUdpConnectionReset, [0], null);
            }
            catch (SocketException) { }
        }

        private static byte[] CreateVoiceFrame(int userId)
        {
            var pcm = new float[ParticipantCodec.FrameSamples];
            var frequency = 180 + userId % 400;
            for (var i = 0; i < pcm.Length; i++)
                pcm[i] = 0.15f * MathF.Sin(2 * MathF.PI * frequency * i / ParticipantCodec.SampleRate);

            Span<byte> encoded = stackalloc byte[ParticipantCodec.MaxEncodedBytes];
            var codec = new ParticipantCodec();
            var bytes = codec.Encode(pcm, encoded);

            if (bytes <= 0)
                throw new InvalidOperationException(
                    "Не удалось подготовить Opus-фрейм для нагрузочного теста.");

            return encoded[..bytes].ToArray();
        }

        public void Dispose() => _client.Dispose();
    }
}