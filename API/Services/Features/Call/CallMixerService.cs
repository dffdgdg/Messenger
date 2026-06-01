namespace API.Services.Features.Call;

public sealed class CallMixerService(ILogger<CallMixerService> logger) : IDisposable
{
    private const int TickMs = 20;
    private const int SilentTicksBeforeReset = 5;

    private readonly ConcurrentDictionary<string, MixerSession> _sessions = new();

    public void RegisterCall(string callId) => _sessions.GetOrAdd(callId, id => new MixerSession(id, logger));

    public void RemoveCall(string callId)
    {
        if (_sessions.TryRemove(callId, out var session))
            session.Dispose();
    }

    public void AddParticipant(string callId, int userId, Action<int, byte[], int> sendMixedAudio)
        => _sessions.GetOrAdd(callId, id => new MixerSession(id, logger)).AddParticipant(userId, sendMixedAudio);

    public void RemoveParticipant(string callId, int userId)
    {
        if (_sessions.TryGetValue(callId, out var session))
            session.RemoveParticipant(userId);
    }

    public void ReceiveAudio(string callId, int userId, ReadOnlySpan<byte> opusData)
    {
        if (_sessions.TryGetValue(callId, out var session))
            session.ReceiveAudio(userId, opusData);
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
            session.Dispose();
        _sessions.Clear();
    }

    private sealed class MixerSession : IDisposable
    {
        private readonly string _callId;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<int, ParticipantState> _participants = new();
        private readonly Timer _timer;
        private readonly Lock _tickLock = new();
        private bool _disposed;

        public MixerSession(string callId, ILogger logger)
        {
            _callId = callId;
            _logger = logger;
            _timer = new Timer(_ => Tick(), null, TickMs, TickMs);
        }

        public void AddParticipant(int userId, Action<int, byte[], int> sendMixedAudio)
            => _participants.AddOrUpdate(userId,
                _ => new ParticipantState(sendMixedAudio),
                (_, existing) => existing.WithSender(sendMixedAudio));

        public void RemoveParticipant(int userId)
        {
            if (_participants.TryRemove(userId, out var state))
                state.Dispose();
        }

        public void ReceiveAudio(int userId, ReadOnlySpan<byte> opusData)
        {
            if (!_participants.TryGetValue(userId, out var state)) return;

            var frame = new float[ParticipantCodec.FrameSamples];
            if (!state.Codec.TryDecode(opusData, frame)) return;

            lock (state.Sync)
            {
                state.LatestFrame = frame;
                state.SilentTicks = 0;
            }
        }

        private void Tick()
        {
            if (_disposed || !Monitor.TryEnter(_tickLock)) return;

            try
            {
                var snapshot = _participants.ToArray();
                if (snapshot.Length == 0) return;

                var frames = new Dictionary<int, float[]>(snapshot.Length);
                foreach (var (userId, state) in snapshot)
                {
                    lock (state.Sync)
                    {
                        if (state.LatestFrame != null)
                            frames[userId] = state.LatestFrame;
                    }
                }

                foreach (var (targetUserId, target) in snapshot)
                {
                    var mix = new float[ParticipantCodec.FrameSamples];
                    var hasAudio = false;

                    foreach (var (sourceUserId, frame) in frames)
                    {
                        if (sourceUserId == targetUserId) continue;

                        hasAudio = true;
                        for (var i = 0; i < mix.Length; i++)
                            mix[i] += frame[i];
                    }

                    if (!hasAudio) continue;

                    Normalize(mix);

                    Span<byte> encoded = stackalloc byte[ParticipantCodec.MaxEncodedBytes];
                    var bytes = target.Codec.Encode(mix, encoded);
                    if (bytes <= 0) continue;

                    var packet = encoded[..bytes].ToArray();
                    target.SendMixedAudio(targetUserId, packet, bytes);
                }

                foreach (var (_, state) in snapshot)
                {
                    lock (state.Sync)
                    {
                        if (state.LatestFrame != null && ++state.SilentTicks >= SilentTicksBeforeReset)
                            state.LatestFrame = null;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка микширования звонка {CallId}", _callId);
            }
            finally
            {
                Monitor.Exit(_tickLock);
            }
        }

        private static void Normalize(float[] mix)
        {
            var peak = 0f;
            for (var i = 0; i < mix.Length; i++)
                peak = Math.Max(peak, Math.Abs(mix[i]));

            if (peak <= 1.0f) return;

            var gain = 1.0f / peak;
            for (var i = 0; i < mix.Length; i++)
                mix[i] *= gain;
        }

        public void Dispose()
        {
            _disposed = true;
            _timer.Dispose();
            foreach (var state in _participants.Values)
                state.Dispose();
            _participants.Clear();
        }
    }

    private sealed class ParticipantState(Action<int, byte[], int> sendMixedAudio) : IDisposable
    {
        public Lock Sync { get; } = new();
        public ParticipantCodec Codec { get; } = new();
        public Action<int, byte[], int> SendMixedAudio { get; private set; } = sendMixedAudio;
        public float[]? LatestFrame { get; set; }
        public int SilentTicks { get; set; }

        public ParticipantState WithSender(Action<int, byte[], int> sendMixedAudio)
        {
            SendMixedAudio = sendMixedAudio;
            return this;
        }

        public void Dispose() { }
    }
}