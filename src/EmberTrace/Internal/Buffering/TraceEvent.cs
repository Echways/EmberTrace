namespace EmberTrace.Internal.Buffering;

internal readonly struct TraceEvent
{
    public readonly int Id;
    public readonly int ThreadId;
    public readonly long Timestamp;
    public readonly TraceEventKind Kind;

    public TraceEvent(int id, int threadId, long timestamp, TraceEventKind kind)
    {
        Id = id;
        ThreadId = threadId;
        Timestamp = timestamp;
        Kind = kind;
    }
}
