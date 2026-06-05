namespace Messenger.LoadTests.VoiceCalls;

public sealed record VoiceCallLoadTestResult(
    VoiceCallLoadTestOptions Options,
    TimeSpan Elapsed,
    long SentPackets,
    long ReceivedMixedPackets,
    long SendErrors,
    int CreatedCalls,
    int JoinedParticipants,
    int FailedJoins,
    double CpuSeconds)
{
    private int N => Options.ParticipantsPerCall;

    /// <summary>
    /// Каждый отправленный пакет должен породить (N-1) mixed-пакетов —
    /// по одному для каждого участника кроме отправителя.
    /// Исключение: единственный говорящий не получает свой голос обратно,
    /// но в тесте все участники говорят одновременно, поэтому формула точная.
    /// </summary>
    public long ExpectedMixedPackets => SentPackets;

    public double ReceiveRatio =>
        ExpectedMixedPackets == 0
            ? 0
            : (double)ReceivedMixedPackets / ExpectedMixedPackets;

    public double SentPacketsPerSecond =>
        Elapsed.TotalSeconds <= 0 ? 0 : SentPackets / Elapsed.TotalSeconds;

    public double ReceivedPacketsPerSecond =>
        Elapsed.TotalSeconds <= 0 ? 0 : ReceivedMixedPackets / Elapsed.TotalSeconds;

    public bool Passed =>
        CreatedCalls == Options.Calls &&
        JoinedParticipants == Options.TotalParticipants &&
        FailedJoins == 0 &&
        SendErrors == 0 &&
        ReceiveRatio >= Options.MinReceiveRatio;

    public void PrintTo(TextWriter writer)
    {
        writer.WriteLine();
        writer.WriteLine("=== Voice calls load test result ===");
        writer.WriteLine($"Status:                 {(Passed ? "PASSED" : "FAILED")}");
        writer.WriteLine($"Calls:                  {CreatedCalls}/{Options.Calls}");
        writer.WriteLine($"Participants:           {JoinedParticipants}/{Options.TotalParticipants}");
        writer.WriteLine($"Failed joins:           {FailedJoins}");
        writer.WriteLine($"Elapsed:                {Elapsed:g}");
        writer.WriteLine($"Sent UDP audio packets: {SentPackets:N0} ({SentPacketsPerSecond:N0}/sec)");
        writer.WriteLine($"Received mixed packets: {ReceivedMixedPackets:N0} ({ReceivedPacketsPerSecond:N0}/sec)");
        writer.WriteLine($"Receive ratio:          {ReceiveRatio:P2} (minimum {Options.MinReceiveRatio:P0})");
        writer.WriteLine($"Expected mixed packets: {ExpectedMixedPackets:N0}");
        writer.WriteLine($"Send errors:            {SendErrors}");
        writer.WriteLine($"Process CPU time:       {CpuSeconds:N2} sec");
    }
}