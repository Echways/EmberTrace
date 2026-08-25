namespace EmberTrace.Testing;

public sealed class TraceIdDelta
{
    public required int Id { get; init; }
    public required bool InBaseline { get; init; }
    public required bool InCurrent { get; init; }

    public required long BaselineCount { get; init; }
    public required long CurrentCount { get; init; }

    public required double BaselineTotalMs { get; init; }
    public required double CurrentTotalMs { get; init; }

    public required double BaselineP95Ms { get; init; }
    public required double CurrentP95Ms { get; init; }

    public required double TotalMsChangePercent { get; init; }
    public required double P95MsChangePercent { get; init; }

    public double TotalMsDelta => CurrentTotalMs - BaselineTotalMs;
    public long CountDelta => CurrentCount - BaselineCount;
    public bool InBoth => InBaseline && InCurrent;
}
