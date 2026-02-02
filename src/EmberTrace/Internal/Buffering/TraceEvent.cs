using EmberTrace.Sessions;

namespace EmberTrace.Internal.Buffering;

internal readonly struct TraceEvent(
    int id,
    int threadId,
    long timestamp,
    TraceEventKind kind,
    long flowId,
    long value,
    long sequence = 0)
{
    public readonly int Id = id;
    public readonly int ThreadId = threadId;
    public readonly long Timestamp = timestamp;
    public readonly TraceEventKind Kind = kind;
    public readonly long FlowId = flowId;
    public readonly long Value = value;
    public readonly long Sequence = sequence;
}
