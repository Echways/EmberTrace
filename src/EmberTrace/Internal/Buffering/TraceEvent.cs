using EmberTrace.Sessions;

namespace EmberTrace.Internal.Buffering;

internal readonly struct TraceEvent(int id, long timestamp, TraceEventKind kind, long flowId, long value)
{
    public readonly long Timestamp = timestamp;
    public readonly long FlowId = flowId;
    public readonly long Value = value;
    public readonly int Id = id;
    public readonly TraceEventKind Kind = kind;
}