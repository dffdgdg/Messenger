using System.Buffers;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace API.Services.Features.Call;

public sealed class CallMixerService(ILogger<CallMixerService> logger) : IDisposable
{
    private const int TickMs = 20;
    private const int SilentTicksBeforeReset = 5;

    private readonly ConcurrentDictionary<string, MixerSession> _sessions = new();

    public void RegisterCall(string callId)
        => _sessions.GetOrAdd(callId, id => new MixerSession(id, logger));

    public void RemoveCall(string callId)
    {
        if (_sessions.TryRemove(callId, out var session))
            session.Dispose();
    }

    public void AddParticipant(string callId, int userId, Action<int, byte[], int> sendMixedAudio)
        => _sessions.GetOrAdd(callId, id => new MixerSession(id, logger))
                    .AddParticipant(userId, sendMixedAudio);

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

    // ════════════════════════════════════════════════════════════════════════
    private sealed class MixerSession : IDisposable
    {
        private readonly string _callId;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<int, ParticipantState> _participants = new();
        private readonly Timer _timer;
        private readonly object _tickLock = new();

        // Все буферы переиспользуются — нулевые аллокации в hot path
        private readonly float[] _mixBuffer = new float[ParticipantCodec.FrameSamples];
        private float[] _personalBuffer = new float[ParticipantCodec.FrameSamples];
        private readonly byte[] _encodedBuffer = new byte[ParticipantCodec.MaxEncodedBytes];
        private readonly byte[] _sharedEncodedBuffer = new byte[ParticipantCodec.MaxEncodedBytes];
        private KeyValuePair<int, ParticipantState>[] _snapshot = [];
        private int[] _frameUserIds = new int[64];
        private float[]?[] _framePcms = new float[]?[64];
        private bool _disposed;

        public MixerSession(string callId, ILogger logger)
        {
            _callId = callId;
            _logger = logger;
            _timer = new Timer(_ => Tick(), null, TickMs, TickMs);
        }

        public void AddParticipant(int userId, Action<int, byte[], int> sendMixedAudio)
            => _participants.AddOrUpdate(
                userId,
                _ => new ParticipantState(sendMixedAudio),
                (_, existing) => existing.WithSender(sendMixedAudio));

        public void RemoveParticipant(int userId)
        {
            if (_participants.TryRemove(userId, out var state))
                state.Dispose();
        }

        /// <summary>
        /// Вызывается из receive-loop relay'а.
        /// Декодирует Opus → PCM и кладёт в канал участника.
        /// Если канал полон — вытесняет старый фрейм (DropOldest).
        /// </summary>
        public void ReceiveAudio(int userId, ReadOnlySpan<byte> opusData)
        {
            if (!_participants.TryGetValue(userId, out var state)) return;

            var frame = ParticipantState.FramePool.Rent(ParticipantCodec.FrameSamples);
            if (!state.Codec.TryDecode(opusData, frame))
            {
                ParticipantState.FramePool.Return(frame);
                return;
            }

            if (!state.FrameChannel.Writer.TryWrite(frame))
            {
                // Канал полон — вытесняем старый фрейм, кладём новый
                if (state.FrameChannel.Reader.TryRead(out var old))
                    ParticipantState.FramePool.Return(old);
                state.FrameChannel.Writer.TryWrite(frame);
            }

            state.SilentTicks = 0;
        }

        private void Tick()
        {
            if (_disposed || !Monitor.TryEnter(_tickLock)) return;
            try
            {
                var count = _participants.Count;
                if (count == 0) return;

                EnsureBufferCapacity(count);

                var written = 0;
                foreach (var kv in _participants)
                    _snapshot[written++] = kv;

                // ── 1. Собираем актуальные фреймы ────────────────────────
                var activeSpeakers = CollectFrames(written);

                // Никто не говорил в этом окне — пропускаем тик
                if (activeSpeakers == 0)
                {
                    ReturnAllFrames(written);
                    return;
                }

                // ── 2. Глобальный микс O(N) ──────────────────────────────
                Array.Clear(_mixBuffer, 0, ParticipantCodec.FrameSamples);
                for (var s = 0; s < written; s++)
                {
                    var srcPcm = _framePcms[s];
                    if (srcPcm == null) continue;
                    ref var mix = ref _mixBuffer[0];
                    ref var src = ref srcPcm[0];
                    for (var i = 0; i < ParticipantCodec.FrameSamples; i++)
                        Unsafe.Add(ref mix, i) += Unsafe.Add(ref src, i);
                }
                Normalize(_mixBuffer);

                // ── 3. Shared encode для молчащих участников ─────────────
                // Молчащие слышат глобальный микс без изменений.
                // Кодируем один раз и переиспользуем буфер — без аллокации.
                var hasListeners = activeSpeakers < written;
                var sharedBytes = 0;
                var hasSharedPacket = false;

                if (hasListeners)
                {
                    sharedBytes = _snapshot[0].Value.Codec.Encode(
                        _mixBuffer.AsSpan(0, ParticipantCodec.FrameSamples),
                        _sharedEncodedBuffer);
                    hasSharedPacket = sharedBytes > 0;
                }

                // ── 4. Рассылка: globalMix − selfVoice для говорящих ─────
                for (var t = 0; t < written; t++)
                {
                    var targetUserId = _frameUserIds[t];
                    var target = _snapshot[t].Value;
                    var targetPcm = _framePcms[t];

                    // Единственный говорящий слышит тишину — пропускаем
                    if (activeSpeakers == 1 && targetPcm != null) continue;

                    if (targetPcm != null)
                    {
                        // Говорящий: вычитаем свой голос из глобального микса
                        ref var mix = ref _mixBuffer[0];
                        ref var src = ref targetPcm[0];
                        ref var dst = ref _personalBuffer[0];
                        for (var i = 0; i < ParticipantCodec.FrameSamples; i++)
                            Unsafe.Add(ref dst, i) = Unsafe.Add(ref mix, i) - Unsafe.Add(ref src, i);

                        Normalize(_personalBuffer);

                        var bytes = target.Codec.Encode(
                            _personalBuffer.AsSpan(0, ParticipantCodec.FrameSamples),
                            _encodedBuffer);
                        if (bytes <= 0) continue;

                        // Единственная неизбежная аллокация: пакет для отправки
                        var packet = new byte[bytes];
                        Buffer.BlockCopy(_encodedBuffer, 0, packet, 0, bytes);
                        target.SendMixedAudio(targetUserId, packet, bytes);
                    }
                    else
                    {
                        // Молчащий: переиспользуем уже закодированный shared-буфер
                        if (!hasSharedPacket) continue;
                        var packet = new byte[sharedBytes];
                        Buffer.BlockCopy(_sharedEncodedBuffer, 0, packet, 0, sharedBytes);
                        target.SendMixedAudio(targetUserId, packet, sharedBytes);
                    }
                }

                // ── 5. Возвращаем PCM-буферы в пул ──────────────────────
                ReturnAllFrames(written);
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

        /// <summary>
        /// Собирает последний фрейм каждого участника из канала.
        /// Промежуточные фреймы возвращаются в пул.
        /// Возвращает количество говорящих участников.
        /// </summary>
        private int CollectFrames(int written)
        {
            var activeSpeakers = 0;
            for (var i = 0; i < written; i++)
            {
                var (userId, state) = _snapshot[i];
                _frameUserIds[i] = userId;

                // Берём самый свежий фрейм, промежуточные возвращаем в пул
                float[]? latest = null;
                while (state.FrameChannel.Reader.TryRead(out var f))
                {
                    if (latest != null)
                        ParticipantState.FramePool.Return(latest);
                    latest = f;
                }

                _framePcms[i] = latest;

                if (latest != null)
                {
                    activeSpeakers++;
                    state.SilentTicks = 0;
                }
                else
                {
                    state.SilentTicks = state.SilentTicks + 1 >= SilentTicksBeforeReset
                        ? 0
                        : state.SilentTicks + 1;
                }
            }
            return activeSpeakers;
        }

        private void EnsureBufferCapacity(int count)
        {
            if (_snapshot.Length >= count) return;
            var newSize = count * 2;
            _snapshot = new KeyValuePair<int, ParticipantState>[newSize];
            _frameUserIds = new int[newSize];
            _framePcms = new float[]?[newSize];
        }

        private void ReturnAllFrames(int written)
        {
            for (var i = 0; i < written; i++)
            {
                if (_framePcms[i] == null) continue;
                ParticipantState.FramePool.Return(_framePcms[i]!);
                _framePcms[i] = null;
            }
        }

        private static void Normalize(float[] mix)
        {
            var peak = 0f;
            for (var i = 0; i < mix.Length; i++)
            {
                var abs = Math.Abs(mix[i]);
                if (abs > peak) peak = abs;
            }
            if (peak <= 1.0f) return;
            var inv = 1.0f / peak;
            for (var i = 0; i < mix.Length; i++)
                mix[i] *= inv;
        }

        private static void Normalize(Span<float> mix)
        {
            var peak = 0f;
            for (var i = 0; i < mix.Length; i++)
            {
                var abs = Math.Abs(mix[i]);
                if (abs > peak) peak = abs;
            }
            if (peak <= 1.0f) return;
            var inv = 1.0f / peak;
            for (var i = 0; i < mix.Length; i++)
                mix[i] *= inv;
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

    // ════════════════════════════════════════════════════════════════════════
    private sealed class ParticipantState(Action<int, byte[], int> sendMixedAudio) : IDisposable
    {
        internal static readonly ArrayPool<float> FramePool = ArrayPool<float>.Shared;

        // Буферизация до 4 фреймов (80ms). DropOldest — теряем старое, не новое.
        internal readonly Channel<float[]> FrameChannel = Channel.CreateBounded<float[]>(
            new BoundedChannelOptions(4)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest,
                AllowSynchronousContinuations = false
            });

        public ParticipantCodec Codec { get; } = new();
        public Action<int, byte[], int> SendMixedAudio { get; private set; } = sendMixedAudio;
        public int SilentTicks { get; set; }

        public ParticipantState WithSender(Action<int, byte[], int> send)
        {
            SendMixedAudio = send;
            return this;
        }

        public void Dispose()
        {
            while (FrameChannel.Reader.TryRead(out var f))
                FramePool.Return(f);
        }
    }
}