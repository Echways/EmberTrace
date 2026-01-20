namespace EmberTrace.Internal.Buffering;

internal readonly struct TraceEvent(int id, int threadId, long timestamp, TraceEventKind kind, long flowId)
{
    public readonly int Id = id;
    public readonly int ThreadId = threadId;
    public readonly long Timestamp = timestamp;
    public readonly TraceEventKind Kind = kind;
    public readonly long FlowId = flowId;
}
