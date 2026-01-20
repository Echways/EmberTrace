namespace EmberTrace.Internal.Buffering;

internal enum TraceEventKind : byte
{
    Begin = 1,
    End = 2,
    FlowStart = 3,
    FlowStep = 4,
    FlowEnd = 5
}