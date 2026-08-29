using System.Text;
using System.Text.Json;
using EmberTrace.Metadata;
using EmberTrace.Sessions;
using static EmberTrace.Export.ChromeJsonWriter;

namespace EmberTrace.Export;

internal static class ChromeTraceExporter
{
    public static void WriteBeginEnd(
        TraceSession session,
        Stream output,
        ITraceMetadataProvider? meta = null,
        bool sortByTimestamp = true,
        int pid = 1,
        string processName = "EmberTrace")
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(output);

        meta ??= session.Metadata;

        var start = session.StartTimestamp;
        var freq = session.TimestampFrequency;

        using var json = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = false });

        json.WriteStartObject();
        json.WriteString("displayTimeUnit", "ms");
        json.WritePropertyName("traceEvents");
        json.WriteStartArray();

        var tracks = CollectTracks(session);
        WriteProcessName(json, pid, processName);
        for (var i = 0; i < tracks.Count; i++)
            WriteThreadName(json, pid, tracks[i].Key, ResolveThreadName(session, tracks[i].Value));

        if (sortByTimestamp)
            foreach (var e in session.EnumerateEventsSorted())
            {
                if (e.Timestamp < start) continue;
                WriteEventBeginEnd(json, e, meta, start, freq, pid);
            }
        else
            foreach (var e in session.EnumerateEvents())
            {
                if (e.Timestamp < start) continue;
                WriteEventBeginEnd(json, e, meta, start, freq, pid);
            }

        json.WriteEndArray();
        json.WriteEndObject();
        json.Flush();
    }

    public static void WriteComplete(
        TraceSession session,
        Stream output,
        ITraceMetadataProvider? meta = null,
        bool sortByStartTimestamp = true,
        int pid = 1,
        string processName = "EmberTrace")
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(output);

        meta ??= session.Metadata;

        var start = session.StartTimestamp;
        var freq = session.TimestampFrequency;

        var complete = new List<CompleteSpan>(checked((int)Math.Min(int.MaxValue, session.EventCount / 2)));
        var asyncSpans = new List<AsyncSpan>();
        ScopeCollector.CollectComplete(new ScopeReader(session), start, complete, asyncSpans);

        if (sortByStartTimestamp)
        {
            complete.Sort(static (a, b) =>
                CompareEventOrder(a.StartTs, a.TrackId, a.Sequence, b.StartTs, b.TrackId, b.Sequence));
            asyncSpans.Sort(static (a, b) =>
                CompareEventOrder(a.StartTs, a.StartTrackId, a.Sequence, b.StartTs, b.StartTrackId, b.Sequence));
        }

        var markers = CollectFlows(session, start);
        markers.Sort(static (a, b) =>
            CompareEventOrder(a.Timestamp, a.TrackId, a.Sequence, b.Timestamp, b.TrackId, b.Sequence));

        using var json = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = false });

        json.WriteStartObject();
        json.WriteString("displayTimeUnit", "ms");
        json.WritePropertyName("traceEvents");
        json.WriteStartArray();

        var tracks = CollectTracks(session, true);
        WriteProcessName(json, pid, processName);
        for (var i = 0; i < tracks.Count; i++)
            WriteThreadName(json, pid, tracks[i].Key, ResolveThreadName(session, tracks[i].Value));

        for (var i = 0; i < markers.Count; i++)
        {
            var e = markers[i];
            switch (e.Kind)
            {
                case TraceEventKind.FlowStart:
                case TraceEventKind.FlowStep:
                case TraceEventKind.FlowEnd:
                    WriteFlowEvent(json, e, meta, start, freq, pid, ChromeEventArgsMode.Detailed);
                    break;
                case TraceEventKind.Instant:
                    WriteInstantEvent(json, e, meta, start, freq, pid);
                    break;
                case TraceEventKind.Counter:
                    WriteCounterEvent(json, e, meta, start, freq, pid);
                    break;
            }
        }

        for (var i = 0; i < asyncSpans.Count; i++)
            WriteAsyncSpan(json, asyncSpans[i], meta, start, freq, pid);

        for (var i = 0; i < complete.Count; i++)
            WriteCompleteEvent(json, complete[i], meta, start, freq, pid, ChromeEventArgsMode.Detailed);

        json.WriteEndArray();
        json.WriteEndObject();
        json.Flush();
    }

    public static string ToJsonBeginEnd(
        TraceSession session,
        ITraceMetadataProvider? meta = null,
        bool sortByTimestamp = true,
        int pid = 1,
        string processName = "EmberTrace")
    {
        using var ms = new MemoryStream(256 * 1024);
        WriteBeginEnd(session, ms, meta, sortByTimestamp, pid, processName);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static string ToJsonComplete(
        TraceSession session,
        ITraceMetadataProvider? meta = null,
        bool sortByStartTimestamp = true,
        int pid = 1,
        string processName = "EmberTrace")
    {
        using var ms = new MemoryStream(256 * 1024);
        WriteComplete(session, ms, meta, sortByStartTimestamp, pid, processName);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static List<KeyValuePair<int, int>> CollectTracks(TraceSession session, bool includeSynthetic = false)
    {
        var threadByTrack = new Dictionary<int, int>();
        foreach (var e in session.EnumerateEvents())
            threadByTrack.TryAdd(e.TrackId, e.ThreadId);

        if (includeSynthetic)
            threadByTrack.TryAdd(0, 0);

        var list = new List<KeyValuePair<int, int>>(threadByTrack);
        list.Sort(static (a, b) => a.Key.CompareTo(b.Key));
        return list;
    }

    private static List<TraceEventRecord> CollectFlows(TraceSession session, long start)
    {
        var list = new List<TraceEventRecord>(256);
        foreach (var e in session.EnumerateEvents())
        {
            if (e.Timestamp < start) continue;
            if (e.Kind == TraceEventKind.FlowStart || e.Kind == TraceEventKind.FlowStep ||
                e.Kind == TraceEventKind.FlowEnd
                || e.Kind == TraceEventKind.Instant || e.Kind == TraceEventKind.Counter)
                list.Add(e);
        }

        return list;
    }

    private static void WriteEventBeginEnd(
        Utf8JsonWriter json,
        TraceEventRecord e,
        ITraceMetadataProvider meta,
        long start,
        long freq,
        int pid)
    {
        switch (e.Kind)
        {
            case TraceEventKind.Begin:
                if (e.AsyncScopeId != 0)
                    WriteAsyncPhase(json, e.Id, e.AsyncScopeId, e.TrackId, e.Timestamp, meta, start, freq, pid, "b");
                else
                    WriteBeginEndEvent(json, e, meta, start, freq, pid, 'B');
                break;
            case TraceEventKind.End:
                if (e.AsyncScopeId != 0)
                    WriteAsyncPhase(json, e.Id, e.AsyncScopeId, e.TrackId, e.Timestamp, meta, start, freq, pid, "e");
                else
                    WriteBeginEndEvent(json, e, meta, start, freq, pid, 'E');
                break;
            case TraceEventKind.FlowStart:
            case TraceEventKind.FlowStep:
            case TraceEventKind.FlowEnd:
                WriteFlowEvent(json, e, meta, start, freq, pid, ChromeEventArgsMode.Detailed);
                break;
            case TraceEventKind.Instant:
                WriteInstantEvent(json, e, meta, start, freq, pid);
                break;
            case TraceEventKind.Counter:
                WriteCounterEvent(json, e, meta, start, freq, pid);
                break;
        }
    }
}
