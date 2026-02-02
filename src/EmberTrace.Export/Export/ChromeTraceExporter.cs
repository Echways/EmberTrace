using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

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
        if (session is null) throw new ArgumentNullException(nameof(session));
        if (output is null) throw new ArgumentNullException(nameof(output));

        meta ??= TraceMetadata.CreateDefault();

        var start = session.StartTimestamp;
        var freq = session.TimestampFrequency;

        using var json = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = false });

        json.WriteStartObject();
        json.WriteString("displayTimeUnit", "ms");
        json.WritePropertyName("traceEvents");
        json.WriteStartArray();

        var tids = CollectThreadIds(session);
        WriteProcessName(json, pid, processName);
        for (int i = 0; i < tids.Count; i++)
            WriteThreadName(json, pid, tids[i], ResolveThreadName(session, tids[i]));

        if (sortByTimestamp)
        {
            foreach (var e in session.EnumerateEventsSorted())
            {
                if (e.Timestamp < start) continue;
                WriteEventBeginEnd(json, e, meta, start, freq, pid);
            }
        }
        else
        {
            foreach (var e in session.EnumerateEvents())
            {
                if (e.Timestamp < start) continue;
                WriteEventBeginEnd(json, e, meta, start, freq, pid);
            }
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
        if (session is null) throw new ArgumentNullException(nameof(session));
        if (output is null) throw new ArgumentNullException(nameof(output));

        meta ??= TraceMetadata.CreateDefault();

        var start = session.StartTimestamp;
        var freq = session.TimestampFrequency;

        var complete = CollectComplete(session, start);
        if (sortByStartTimestamp)
            complete.Sort(static (a, b) => CompareEventOrder(a.StartTs, a.ThreadId, a.Sequence, b.StartTs, b.ThreadId, b.Sequence));

        var markers = CollectFlows(session, start);
        markers.Sort(static (a, b) => CompareEventOrder(a.Timestamp, a.ThreadId, a.Sequence, b.Timestamp, b.ThreadId, b.Sequence));

        using var json = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = false });

        json.WriteStartObject();
        json.WriteString("displayTimeUnit", "ms");
        json.WritePropertyName("traceEvents");
        json.WriteStartArray();

        var tids = CollectThreadIds(session, includeSynthetic: true);
        WriteProcessName(json, pid, processName);
        for (int i = 0; i < tids.Count; i++)
            WriteThreadName(json, pid, tids[i], ResolveThreadName(session, tids[i]));

        for (int i = 0; i < markers.Count; i++)
        {
            var e = markers[i];
            switch (e.Kind)
            {
                case TraceEventKind.FlowStart:
                case TraceEventKind.FlowStep:
                case TraceEventKind.FlowEnd:
                    WriteFlowEvent(json, e, meta, start, freq, pid);
                    break;
                case TraceEventKind.Instant:
                    WriteInstantEvent(json, e, meta, start, freq, pid);
                    break;
                case TraceEventKind.Counter:
                    WriteCounterEvent(json, e, meta, start, freq, pid);
                    break;
            }
        }

        for (int i = 0; i < complete.Count; i++)
            WriteCompleteEvent(json, complete[i], meta, start, freq, pid);

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
        using var ms = new MemoryStream(capacity: 256 * 1024);
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
        using var ms = new MemoryStream(capacity: 256 * 1024);
        WriteComplete(session, ms, meta, sortByStartTimestamp, pid, processName);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private readonly struct Frame
    {
        public readonly int Id;
        public readonly long Start;
        public readonly long Sequence;

        public Frame(int id, long start, long sequence)
        {
            Id = id;
            Start = start;
            Sequence = sequence;
        }
    }

    private readonly struct CompleteEvent
    {
        public readonly int Id;
        public readonly int ThreadId;
        public readonly long StartTs;
        public readonly long DurTicks;
        public readonly long Sequence;

        public CompleteEvent(int id, int threadId, long startTs, long durTicks, long sequence)
        {
            Id = id;
            ThreadId = threadId;
            StartTs = startTs;
            DurTicks = durTicks;
            Sequence = sequence;
        }
    }

    private static List<int> CollectThreadIds(TraceSession session, bool includeSynthetic = false)
    {
        var set = new HashSet<int>();
        foreach (var e in session.EnumerateEvents())
            set.Add(e.ThreadId);

        var list = new List<int>(set);
        list.Sort();

        if (includeSynthetic)
        {
            if (!set.Contains(0))
                list.Insert(0, 0);
        }

        return list;
    }

    private static List<TraceEventRecord> CollectEvents(TraceSession session, long start)
    {
        var list = new List<TraceEventRecord>(checked((int)Math.Min(int.MaxValue, session.EventCount)));
        foreach (var e in session.EnumerateEvents())
        {
            if (e.Timestamp < start) continue;
            list.Add(e);
        }
        return list;
    }

    private static List<CompleteEvent> CollectComplete(TraceSession session, long start)
    {
        var stacks = new Dictionary<int, List<Frame>>(capacity: 8);
        var list = new List<CompleteEvent>(capacity: checked((int)Math.Min(int.MaxValue, session.EventCount / 2)));

        foreach (var e in session.EnumerateEvents())
        {
            if (e.Timestamp < start) continue;
            if (e.Kind != TraceEventKind.Begin && e.Kind != TraceEventKind.End) continue;

            if (!stacks.TryGetValue(e.ThreadId, out var stack))
            {
                stack = new List<Frame>(capacity: 64);
                stacks.Add(e.ThreadId, stack);
            }

            if (e.Kind == TraceEventKind.Begin)
            {
                stack.Add(new Frame(e.Id, e.Timestamp, e.Sequence));
                continue;
            }

            if (stack.Count == 0)
                continue;

            var top = stack[^1];
            if (top.Id != e.Id)
            {
                var idx = -1;
                for (int s = stack.Count - 2; s >= 0; s--)
                {
                    if (stack[s].Id == e.Id)
                    {
                        idx = s;
                        break;
                    }
                }

                if (idx < 0)
                    continue;

                stack.RemoveRange(idx + 1, stack.Count - (idx + 1));
                top = stack[^1];
            }

            stack.RemoveAt(stack.Count - 1);

            var dur = e.Timestamp - top.Start;
            if (dur < 0) continue;

            list.Add(new CompleteEvent(e.Id, e.ThreadId, top.Start, dur, top.Sequence));
        }

        return list;
    }

    private static List<TraceEventRecord> CollectFlows(TraceSession session, long start)
    {
        var list = new List<TraceEventRecord>(capacity: 256);
        foreach (var e in session.EnumerateEvents())
        {
            if (e.Timestamp < start) continue;
            if (e.Kind == TraceEventKind.FlowStart || e.Kind == TraceEventKind.FlowStep || e.Kind == TraceEventKind.FlowEnd
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
                WriteBeginEndEvent(json, e, meta, start, freq, pid, phase: 'B');
                break;
            case TraceEventKind.End:
                WriteBeginEndEvent(json, e, meta, start, freq, pid, phase: 'E');
                break;
            case TraceEventKind.FlowStart:
            case TraceEventKind.FlowStep:
            case TraceEventKind.FlowEnd:
                WriteFlowEvent(json, e, meta, start, freq, pid);
                break;
            case TraceEventKind.Instant:
                WriteInstantEvent(json, e, meta, start, freq, pid);
                break;
            case TraceEventKind.Counter:
                WriteCounterEvent(json, e, meta, start, freq, pid);
                break;
        }
    }

    private static void WriteBeginEndEvent(
        Utf8JsonWriter json,
        TraceEventRecord e,
        ITraceMetadataProvider meta,
        long start,
        long freq,
        int pid,
        char phase)
    {
        Resolve(meta, e.Id, out var name, out var cat);

        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteString("cat", cat);
        json.WriteString("ph", phase.ToString());
        json.WriteNumber("ts", ToUs(e.Timestamp - start, freq));
        json.WriteNumber("pid", pid);
        json.WriteNumber("tid", e.ThreadId);
        json.WriteEndObject();
    }

    private static void WriteCompleteEvent(
        Utf8JsonWriter json,
        CompleteEvent e,
        ITraceMetadataProvider meta,
        long start,
        long freq,
        int pid)
    {
        Resolve(meta, e.Id, out var name, out var cat);

        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteString("cat", cat);
        json.WriteString("ph", "X");
        json.WriteNumber("ts", ToUs(e.StartTs - start, freq));
        json.WriteNumber("dur", ToUs(e.DurTicks, freq));
        json.WriteNumber("pid", pid);
        json.WriteNumber("tid", e.ThreadId);
        json.WriteEndObject();
    }

    private static void WriteFlowEvent(
        Utf8JsonWriter json,
        TraceEventRecord e,
        ITraceMetadataProvider meta,
        long start,
        long freq,
        int pid)
    {
        Resolve(meta, e.Id, out var name, out var cat);

        var ph = e.Kind switch
        {
            TraceEventKind.FlowStart => "s",
            TraceEventKind.FlowStep => "t",
            TraceEventKind.FlowEnd => "f",
            _ => "t"
        };

        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteString("cat", cat);
        json.WriteString("ph", ph);
        json.WriteNumber("ts", ToUs(e.Timestamp - start, freq));
        json.WriteNumber("pid", pid);
        json.WriteNumber("tid", e.ThreadId);
        json.WriteNumber("id", e.FlowId);
        json.WriteEndObject();
    }

    private static void WriteInstantEvent(
        Utf8JsonWriter json,
        TraceEventRecord e,
        ITraceMetadataProvider meta,
        long start,
        long freq,
        int pid)
    {
        Resolve(meta, e.Id, out var name, out var cat);

        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteString("cat", cat);
        json.WriteString("ph", "i");
        json.WriteNumber("ts", ToUs(e.Timestamp - start, freq));
        json.WriteNumber("pid", pid);
        json.WriteNumber("tid", e.ThreadId);
        json.WriteString("s", "t");
        json.WriteEndObject();
    }

    private static void WriteCounterEvent(
        Utf8JsonWriter json,
        TraceEventRecord e,
        ITraceMetadataProvider meta,
        long start,
        long freq,
        int pid)
    {
        Resolve(meta, e.Id, out var name, out var cat);

        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteString("cat", cat);
        json.WriteString("ph", "C");
        json.WriteNumber("ts", ToUs(e.Timestamp - start, freq));
        json.WriteNumber("pid", pid);
        json.WriteNumber("tid", e.ThreadId);
        json.WritePropertyName("args");
        json.WriteStartObject();
        json.WriteNumber("value", e.Value);
        json.WriteEndObject();
        json.WriteEndObject();
    }

    private static void WriteProcessName(Utf8JsonWriter json, int pid, string name)
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

    private static void WriteThreadName(Utf8JsonWriter json, int pid, int tid, string name)
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

    private static string ResolveThreadName(TraceSession session, int tid)
    {
        if (session.ThreadNames.TryGetValue(tid, out var name) && !string.IsNullOrWhiteSpace(name))
            return name;

        return $"Thread {tid}";
    }

    private static int CompareEventOrder(long timestamp, int threadId, long sequence, long otherTimestamp, int otherThreadId, long otherSequence)
    {
        var cmp = timestamp.CompareTo(otherTimestamp);
        if (cmp != 0) return cmp;
        cmp = threadId.CompareTo(otherThreadId);
        if (cmp != 0) return cmp;
        return sequence.CompareTo(otherSequence);
    }

    private static double ToUs(long ticks, long freq) => ticks * 1_000_000.0 / freq;

    private static void Resolve(ITraceMetadataProvider meta, int id, out string name, out string category)
    {
        if (meta.TryGet(id, out var m))
        {
            name = m.Name;
            category = m.Category ?? "";
            return;
        }

        name = id.ToString();
        category = "";
    }
}
