namespace EmberTrace.Sessions;

public enum TraceEventKind : byte
{
    Begin = 1,
    End = 2,
    FlowStart = 3,
    FlowStep = 4,
    FlowEnd = 5
}
