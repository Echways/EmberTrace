using System.Text.Json;
using EmberTrace.Internal.Time;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.Export;

internal static class ChromeJsonWriter
{
    public static void WriteCompleteEvent(
        Utf8JsonWriter json,
        in CompleteSpan e,
        ITraceMetadataProvider? meta,
        long baseTs,
        long freq,
        int pid,
        ChromeEventArgsMode args)
    {
        var converter = new TickConverter(freq);
        meta.Resolve(e.Id, out var name, out var cat);

        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteString("cat", cat);
        json.WriteString("ph", "X");
        json.WriteNumber("ts", converter.ToUs(e.StartTs - baseTs));
        json.WriteNumber("dur", converter.ToUs(e.DurTicks));
        json.WriteNumber("pid", pid);
        json.WriteNumber("tid", e.TrackId);

        if (args == ChromeEventArgsMode.Detailed)
        {
            json.WritePropertyName("args");
            json.WriteStartObject();
            json.WriteNumber("id", e.Id);
            json.WriteNumber("depth", e.Depth);
            if (e.ParentId != 0)
                json.WriteNumber("parent", e.ParentId);
            json.WriteEndObject();
        }

        json.WriteEndObject();
    }

    public static void WriteBeginEndEvent(
        Utf8JsonWriter json,
        in TraceEventRecord e,
        ITraceMetadataProvider? meta,
        long baseTs,
        long freq,
        int pid,
        char phase)
    {
        meta.Resolve(e.Id, out var name, out var cat);

        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteString("cat", cat);
        json.WriteString("ph", phase.ToString());
        json.WriteNumber("ts", new TickConverter(freq).ToUs(e.Timestamp - baseTs));
        json.WriteNumber("pid", pid);
        json.WriteNumber("tid", e.TrackId);
        json.WriteEndObject();
    }

    public static void WriteAsyncSpan(
        Utf8JsonWriter json,
        in AsyncSpan span,
        ITraceMetadataProvider? meta,
        long baseTs,
        long freq,
        int pid)
    {
        WriteAsyncPhase(json, span.Id, span.AsyncScopeId, span.StartTrackId, span.StartTs, meta, baseTs, freq, pid, "b");
        WriteAsyncPhase(json, span.Id, span.AsyncScopeId, span.EndTrackId, span.EndTs, meta, baseTs, freq, pid, "e");
    }

    public static void WriteAsyncPhase(
        Utf8JsonWriter json,
        int id,
        long asyncScopeId,
        int trackId,
        long timestamp,
        ITraceMetadataProvider? meta,
        long baseTs,
        long freq,
        int pid,
        string phase)
    {
        meta.Resolve(id, out var name, out var cat);

        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteString("cat", cat);
        json.WriteString("ph", phase);
        json.WriteNumber("ts", new TickConverter(freq).ToUs(timestamp - baseTs));
        json.WriteNumber("pid", pid);
        json.WriteNumber("tid", trackId);
        json.WriteNumber("id", asyncScopeId);
        json.WriteEndObject();
    }

    public static void WriteFlowEvent(
        Utf8JsonWriter json,
        in TraceEventRecord e,
        ITraceMetadataProvider? meta,
        long baseTs,
        long freq,
        int pid,
        ChromeEventArgsMode args)
    {
        var phase = e.Kind switch
        {
            TraceEventKind.FlowStart => "s",
            TraceEventKind.FlowStep => "t",
            TraceEventKind.FlowEnd => "f",
            _ => "t"
        };

        WriteFlowEvent(json, e.Id, e.TrackId, e.Timestamp, e.FlowId, phase, meta, baseTs, freq, pid, args);
    }

    public static void WriteFlowEvent(
        Utf8JsonWriter json,
        int id,
        int trackId,
        long timestamp,
        long flowId,
        string phase,
        ITraceMetadataProvider? meta,
        long baseTs,
        long freq,
        int pid,
        ChromeEventArgsMode args)
    {
        meta.Resolve(id, out var name, out var cat);

        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteString("cat", cat);
        json.WriteString("ph", phase);
        json.WriteNumber("ts", new TickConverter(freq).ToUs(timestamp - baseTs));
        json.WriteNumber("pid", pid);
        json.WriteNumber("tid", trackId);
        json.WriteNumber("id", flowId);

        if (args == ChromeEventArgsMode.Detailed)
        {
            json.WritePropertyName("args");
            json.WriteStartObject();
            json.WriteNumber("id", id);
            json.WriteEndObject();
        }

        json.WriteEndObject();
    }

    public static void WriteInstantEvent(
        Utf8JsonWriter json,
        in TraceEventRecord e,
        ITraceMetadataProvider? meta,
        long baseTs,
        long freq,
        int pid)
    {
        meta.Resolve(e.Id, out var name, out var cat);

        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteString("cat", cat);
        json.WriteString("ph", "i");
        json.WriteNumber("ts", new TickConverter(freq).ToUs(e.Timestamp - baseTs));
        json.WriteNumber("pid", pid);
        json.WriteNumber("tid", e.TrackId);
        json.WriteString("s", "t");
        json.WriteEndObject();
    }

    public static void WriteCounterEvent(
        Utf8JsonWriter json,
        in TraceEventRecord e,
        ITraceMetadataProvider? meta,
        long baseTs,
        long freq,
        int pid)
    {
        meta.Resolve(e.Id, out var name, out var cat);

        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteString("cat", cat);
        json.WriteString("ph", "C");
        json.WriteNumber("ts", new TickConverter(freq).ToUs(e.Timestamp - baseTs));
        json.WriteNumber("pid", pid);
        json.WriteNumber("tid", e.TrackId);
        json.WritePropertyName("args");
        json.WriteStartObject();
        json.WriteNumber("value", e.Value);
        json.WriteEndObject();
        json.WriteEndObject();
    }

    public static void WriteSyntheticTopLevel(
        Utf8JsonWriter json,
        int pid,
        long minTs,
        long maxTs,
        long freq,
        int markerId,
        string markerName)
    {
        json.WriteStartObject();
        json.WriteString("name", markerName);
        json.WriteString("cat", "Marked");
        json.WriteString("ph", "X");
        json.WriteNumber("ts", 0);
        json.WriteNumber("dur", new TickConverter(freq).ToUs(maxTs - minTs));
        json.WriteNumber("pid", pid);
        json.WriteNumber("tid", 0);
        json.WritePropertyName("args");
        json.WriteStartObject();
        json.WriteNumber("id", markerId);
        json.WriteEndObject();
        json.WriteEndObject();
    }

    public static void WriteProcessName(Utf8JsonWriter json, int pid, string name)
    {
        json.WriteStartObject();
        json.WriteString("name", "process_name");
        json.WriteString("ph", "M");
        json.WriteNumber("pid", pid);
        json.WriteNumber("tid", 0);
        json.WritePropertyName("args");
        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteEndObject();
        json.WriteEndObject();
    }

    public static void WriteThreadName(Utf8JsonWriter json, int pid, int tid, string name)
    {
        json.WriteStartObject();
        json.WriteString("name", "thread_name");
        json.WriteString("ph", "M");
        json.WriteNumber("pid", pid);
        json.WriteNumber("tid", tid);
        json.WritePropertyName("args");
        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteEndObject();
        json.WriteEndObject();
    }

    public static string ResolveThreadName(TraceSession session, int threadId)
    {
        if (session.ThreadNames.TryGetValue(threadId, out var name) && !string.IsNullOrWhiteSpace(name))
            return name;

        return $"Thread {threadId}";
    }

    public static int CompareEventOrder(
        long timestamp,
        int trackId,
        long sequence,
        long otherTimestamp,
        int otherTrackId,
        long otherSequence)
    {
        var cmp = timestamp.CompareTo(otherTimestamp);
        if (cmp != 0) return cmp;
        cmp = trackId.CompareTo(otherTrackId);
        if (cmp != 0) return cmp;
        return sequence.CompareTo(otherSequence);
    }
}
