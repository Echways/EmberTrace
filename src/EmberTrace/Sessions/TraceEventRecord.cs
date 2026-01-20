namespace EmberTrace.Sessions;

public readonly record struct TraceEventRecord(
    int Id,
    int ThreadId,
    long Timestamp,
    TraceEventKind Kind,
    long FlowId);
