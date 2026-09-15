using EmberTrace.Sessions;

namespace EmberTrace.Extensions.Hosting.Tests;

internal static class SessionEvents
{
    public static List<TraceEventRecord> SortedEvents(this TraceSession session)
    {
        var events = new List<TraceEventRecord>();
        foreach (var e in session.EnumerateEventsSorted())
            events.Add(e);

        return events;
    }
}
