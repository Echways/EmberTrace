using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using EmberTrace.Internal.Buffering;
using EmberTrace.Internal.Time;
using EmberTrace.Public;

namespace EmberTrace.Reporting.Export;

public static class ChromeTraceExporter
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
        var freq = Timestamp.Frequency;

        using var json = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = false });

        json.WriteStartObject();
        json.WriteString("displayTimeUnit", "ms");
        json.WritePropertyName("traceEvents");
        json.WriteStartArray();

        var tids = CollectThreadIds(session, includeSynthetic: false);
        WriteProcessName(json, pid, processName);
        foreach (var tid in tids)
            WriteThreadName(json, pid, tid, $"Thread {tid}");

        if (sortByTimestamp)
        {
            var events = CollectRawEvents(session, start);
            events.Sort(static (a, b) =>
            {
                var c = a.Timestamp.CompareTo(b.Timestamp);
                if (c != 0) return c;

                var pa = PhaseRank(a.Kind);
                var pb = PhaseRank(b.Kind);
                c = pa.CompareTo(pb);
                if (c != 0) return c;

                return a.ThreadId.CompareTo(b.ThreadId);
            });

            for (int i = 0; i < events.Count; i++)
                WriteRawEvent(json, events[i], meta, start, freq, pid);
        }
        else
        {
            var chunks = session.Chunks;
            for (int ci = 0; ci < chunks.Count; ci++)
            {
                var c = chunks[ci];
                var arr = c.Events;
                for (int i = 0; i < c.Count; i++)
                    WriteRawEvent(json, arr[i], meta, start, freq, pid);
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
        var freq = Timestamp.Frequency;

        var complete = CollectComplete(session, start);
        if (sortByStartTimestamp)
            complete.Sort(static (a, b) => a.StartTs.CompareTo(b.StartTs));

        using var json = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = false });

        json.WriteStartObject();
        json.WriteString("displayTimeUnit", "ms");
        json.WritePropertyName("traceEvents");
        json.WriteStartArray();

        var tids = CollectThreadIds(session, includeSynthetic: true);
        WriteProcessName(json, pid, processName);
        foreach (var tid in tids)
            WriteThreadName(json, pid, tid, $"Thread {tid}");

        var flows = CollectFlowEvents(session, start);
        flows.Sort(static (a, b) => a.Timestamp.CompareTo(b.Timestamp));
        for (int i = 0; i < flows.Count; i++)
            WriteFlowEvent(json, flows[i], meta, start, freq, pid);

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

    private static List<TraceEvent> CollectRawEvents(TraceSession session, long start)
    {
        var list = new List<TraceEvent>(checked((int)Math.Min(int.MaxValue, session.EventCount)));

        var chunks = session.Chunks;
        for (int ci = 0; ci < chunks.Count; ci++)
        {
            var c = chunks[ci];
            var arr = c.Events;
            for (int i = 0; i < c.Count; i++)
            {
                var e = arr[i];
                if (e.Timestamp < start)
                    continue;
                list.Add(e);
            }
        }

        return list;
    }

    private static List<CompleteEvent> CollectComplete(TraceSession session, long start)
    {
        var stacks = new Dictionary<int, List<Frame>>(capacity: 8);
        var list = new List<CompleteEvent>(capacity: checked((int)Math.Min(int.MaxValue, session.EventCount / 2)));

        var chunks = session.Chunks;
        for (int ci = 0; ci < chunks.Count; ci++)
        {
            var c = chunks[ci];
            var arr = c.Events;

            for (int i = 0; i < c.Count; i++)
            {
                var e = arr[i];
                if (e.Timestamp < start)
                    continue;

                if (e.Kind != TraceEventKind.Begin && e.Kind != TraceEventKind.End)
                    continue;

                if (!stacks.TryGetValue(e.ThreadId, out var stack))
                {
                    stack = new List<Frame>(capacity: 64);
                    stacks.Add(e.ThreadId, stack);
                }

                if (e.Kind == TraceEventKind.Begin)
                {
                    stack.Add(new Frame(e.Id, e.Timestamp));
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

                var depth = stack.Count - 1;
                var parentId = depth > 0 ? stack[depth - 1].Id : 0;

                stack.RemoveAt(stack.Count - 1);

                var dur = e.Timestamp - top.Start;
                if (dur <= 0)
                    continue;

                list.Add(new CompleteEvent(e.Id, e.ThreadId, top.Start, dur, depth, parentId));
            }
        }

        return list;
    }

    private static List<FlowEvent> CollectFlowEvents(TraceSession session, long start)
    {
        var list = new List<FlowEvent>(capacity: checked((int)Math.Min(int.MaxValue, session.EventCount)));

        var chunks = session.Chunks;
        for (int ci = 0; ci < chunks.Count; ci++)
        {
            var c = chunks[ci];
            var arr = c.Events;

            for (int i = 0; i < c.Count; i++)
            {
                var e = arr[i];
                if (e.Timestamp < start)
                    continue;

                if (e.Kind != TraceEventKind.FlowStart && e.Kind != TraceEventKind.FlowStep && e.Kind != TraceEventKind.FlowEnd)
                    continue;

                if (e.FlowId == 0)
                    continue;

                list.Add(new FlowEvent(e.Id, e.ThreadId, e.Timestamp, e.FlowId, FlowPhaseOf(e.Kind)));
            }
        }

        return list;
    }

    private static HashSet<int> CollectThreadIds(TraceSession session, bool includeSynthetic)
    {
        var tids = new HashSet<int>();

        var chunks = session.Chunks;
        for (int ci = 0; ci < chunks.Count; ci++)
        {
            var c = chunks[ci];
            var arr = c.Events;
            for (int i = 0; i < c.Count; i++)
            {
                var e = arr[i];
                tids.Add(e.ThreadId);
            }
        }

        if (includeSynthetic)
            tids.Add(0);

        return tids;
    }

    private static void WriteRawEvent(
        Utf8JsonWriter json,
        in TraceEvent e,
        ITraceMetadataProvider meta,
        long start,
        long freq,
        int pid)
    {
        if (e.Kind == TraceEventKind.Begin || e.Kind == TraceEventKind.End)
        {
            WriteBeginEndEvent(json, e, meta, start, freq, pid);
            return;
        }

        if (e.Kind == TraceEventKind.FlowStart || e.Kind == TraceEventKind.FlowStep || e.Kind == TraceEventKind.FlowEnd)
        {
            if (e.FlowId == 0)
                return;

            var fe = new FlowEvent(e.Id, e.ThreadId, e.Timestamp, e.FlowId, FlowPhaseOf(e.Kind));
            WriteFlowEvent(json, fe, meta, start, freq, pid);
        }
    }

    private static void WriteBeginEndEvent(
        Utf8JsonWriter json,
        in TraceEvent e,
        ITraceMetadataProvider meta,
        long start,
        long freq,
        int pid)
    {
        var tsUs = ToUs(e.Timestamp - start, freq);
        Resolve(meta, e.Id, out var name, out var cat);

        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteString("cat", cat);
        json.WriteString("ph", e.Kind == TraceEventKind.Begin ? "B" : "E");
        json.WriteNumber("ts", tsUs);
        json.WriteNumber("pid", pid);
        json.WriteNumber("tid", e.ThreadId);
        json.WriteEndObject();
    }

    private static void WriteCompleteEvent(
        Utf8JsonWriter json,
        in CompleteEvent e,
        ITraceMetadataProvider meta,
        long start,
        long freq,
        int pid)
    {
        var tsUs = ToUs(e.StartTs - start, freq);
        var durUs = ToUs(e.Dur, freq);
        Resolve(meta, e.Id, out var name, out var cat);

        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteString("cat", cat);
        json.WriteString("ph", "X");
        json.WriteNumber("ts", tsUs);
        json.WriteNumber("dur", durUs);
        json.WriteNumber("pid", pid);
        json.WriteNumber("tid", e.Tid);

        json.WritePropertyName("args");
        json.WriteStartObject();
        json.WriteNumber("id", e.Id);
        json.WriteNumber("depth", e.Depth);
        if (e.ParentId != 0)
            json.WriteNumber("parent", e.ParentId);
        json.WriteEndObject();

        json.WriteEndObject();
    }

    private static void WriteFlowEvent(
        Utf8JsonWriter json,
        in FlowEvent e,
        ITraceMetadataProvider meta,
        long start,
        long freq,
        int pid)
    {
        var tsUs = ToUs(e.Timestamp - start, freq);
        Resolve(meta, e.Id, out var name, out var cat);

        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteString("cat", cat);
        json.WriteString("ph", e.Phase);
        json.WriteNumber("ts", tsUs);
        json.WriteNumber("pid", pid);
        json.WriteNumber("tid", e.Tid);
        json.WriteNumber("id", e.FlowId);

        json.WritePropertyName("args");
        json.WriteStartObject();
        json.WriteNumber("id", e.Id);
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

    private static long ToUs(long deltaTicks, long freq)
    {
        if (deltaTicks <= 0)
            return 0;

        return (deltaTicks * 1_000_000L) / freq;
    }

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

    private static int PhaseRank(TraceEventKind kind)
    {
        return kind switch
        {
            TraceEventKind.FlowStart => 0,
            TraceEventKind.Begin => 1,
            TraceEventKind.FlowStep => 2,
            TraceEventKind.End => 3,
            TraceEventKind.FlowEnd => 4,
            _ => 5
        };
    }

    private static string FlowPhaseOf(TraceEventKind kind)
    {
        return kind switch
        {
            TraceEventKind.FlowStart => "s",
            TraceEventKind.FlowStep => "t",
            TraceEventKind.FlowEnd => "f",
            _ => "t"
        };
    }

    private readonly struct CompleteEvent
    {
        public readonly int Id;
        public readonly int Tid;
        public readonly long StartTs;
        public readonly long Dur;
        public readonly int Depth;
        public readonly int ParentId;

        public CompleteEvent(int id, int tid, long startTs, long dur, int depth, int parentId)
        {
            Id = id;
            Tid = tid;
            StartTs = startTs;
            Dur = dur;
            Depth = depth;
            ParentId = parentId;
        }
    }

    private readonly struct FlowEvent
    {
        public readonly int Id;
        public readonly int Tid;
        public readonly long Timestamp;
        public readonly long FlowId;
        public readonly string Phase;

        public FlowEvent(int id, int tid, long timestamp, long flowId, string phase)
        {
            Id = id;
            Tid = tid;
            Timestamp = timestamp;
            FlowId = flowId;
            Phase = phase;
        }
    }

    private readonly struct Frame
    {
        public readonly int Id;
        public readonly long Start;

        public Frame(int id, long start)
        {
            Id = id;
            Start = start;
        }
    }
}
