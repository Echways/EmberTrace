namespace EmberTrace.Sessions;

public readonly record struct TraceEventRecord(
    int Id,
    int ThreadId,
    long Timestamp,
    TraceEventKind Kind,
    long FlowId,
    long Value,
    long Sequence = 0,
    int TrackId = 0)
{
    public int TrackId { get; init; } = TrackId == 0 ? ThreadId : TrackId;

    public bool IsScope => Kind == TraceEventKind.Begin || Kind == TraceEventKind.End;

    public long AsyncScopeId => IsScope ? FlowId : 0;

    public long AsyncContextId => IsScope ? Value : 0;
}
