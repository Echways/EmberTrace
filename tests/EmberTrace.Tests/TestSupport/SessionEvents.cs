using EmberTrace.Sessions;

namespace EmberTrace.Tests;

internal static class SessionEvents
{
    public static List<TraceEventRecord> Events(this TraceSession session)
    {
        var events = new List<TraceEventRecord>();
        foreach (var e in session.EnumerateEvents())
            events.Add(e);

        return events;
    }

    public static List<TraceEventRecord> SortedEvents(this TraceSession session)
    {
        var events = new List<TraceEventRecord>();
        foreach (var e in session.EnumerateEventsSorted())
            events.Add(e);

        return events;
    }
}
