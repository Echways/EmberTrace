using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.Tests;

internal sealed class TraceScript(long frequency = 1_000_000)
{
    private readonly List<TraceEventRecord> _events = [];
    private readonly Dictionary<int, long> _sequences = [];

    public TraceScript Begin(int id, long timestamp, int thread = 1)
    {
        return Add(id, thread, timestamp, TraceEventKind.Begin, 0, 0);
    }

    public TraceScript End(int id, long timestamp, int thread = 1)
    {
        return Add(id, thread, timestamp, TraceEventKind.End, 0, 0);
    }

    public TraceScript Span(int id, long start, long end, int thread = 1)
    {
        return Begin(id, start, thread).End(id, end, thread);
    }

    public TraceScript Instant(int id, long timestamp, int thread = 1)
    {
        return Add(id, thread, timestamp, TraceEventKind.Instant, 0, 0);
    }

    public TraceScript Counter(int id, long timestamp, long value, int thread = 1)
    {
        return Add(id, thread, timestamp, TraceEventKind.Counter, 0, value);
    }

    public TraceScript Flow(TraceEventKind kind, int id, long timestamp, long flowId, int thread = 1)
    {
        return Add(id, thread, timestamp, kind, flowId, 0);
    }

    public TraceSession ToSession(
        long start = 0,
        long? end = null,
        ITraceMetadataProvider? metadata = null,
        IReadOnlyDictionary<int, string>? threadNames = null)
    {
        var ordered = _events.OrderBy(e => e.Timestamp).ThenBy(e => e.ThreadId).ThenBy(e => e.Sequence).ToList();
        var last = ordered.Count == 0 ? start : ordered[^1].Timestamp;
        return TraceSession.FromEvents(ordered, start, end ?? last, frequency, threadNames, metadata);
    }

    private TraceScript Add(int id, int thread, long timestamp, TraceEventKind kind, long flowId, long value)
    {
        var sequence = _sequences.GetValueOrDefault(thread) + 1;
        _sequences[thread] = sequence;
        _events.Add(new TraceEventRecord(id, thread, timestamp, kind, flowId, value, sequence));
        return this;
    }
}
