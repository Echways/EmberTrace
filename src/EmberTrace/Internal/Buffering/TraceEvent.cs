namespace EmberTrace.Internal.Buffering;

internal readonly struct TraceEvent
{
    public readonly int Id;
    public readonly long Timestamp;
    public readonly TraceEventKind Kind;

    public TraceEvent(int id, long timestamp, TraceEventKind kind)
    {
        Id = id;
        Timestamp = timestamp;
        Kind = kind;
    }
}
