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
        int pid = 1)
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

        if (sortByTimestamp)
        {
            var events = CollectBeginEnd(session, start);
            events.Sort(static (a, b) =>
            {
                var c = a.Ts.CompareTo(b.Ts);
                if (c != 0) return c;
                return a.Phase.CompareTo(b.Phase);
            });

            for (int i = 0; i < events.Count; i++)
                WriteBeginEndEvent(json, events[i], meta, start, freq, pid);
        }
        else
        {
            var chunks = session.Chunks;
            for (int ci = 0; ci < chunks.Count; ci++)
            {
                var c = chunks[ci];
                var arr = c.Events;
                for (int i = 0; i < c.Count; i++)
                    WriteBeginEndEvent(json, arr[i], meta, start, freq, pid);
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

        var tids = new HashSet<int>();
        for (int i = 0; i < complete.Count; i++)
            tids.Add(complete[i].Tid);

        WriteProcessName(json, pid, processName);
        foreach (var tid in tids)
            WriteThreadName(json, pid, tid, $"Thread {tid}");

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
        int pid = 1)
    {
        using var ms = new MemoryStream(capacity: 256 * 1024);
        WriteBeginEnd(session, ms, meta, sortByTimestamp, pid);
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

    private static List<BeginEndEvent> CollectBeginEnd(TraceSession session, long start)
    {
        var list = new List<BeginEndEvent>(checked((int)Math.Min(int.MaxValue, session.EventCount)));

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

                list.Add(new BeginEndEvent(e.Id, e.ThreadId, e.Timestamp, e.Kind == TraceEventKind.Begin ? (byte)0 : (byte)1));
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

    private static void WriteBeginEndEvent(
        Utf8JsonWriter json,
        in BeginEndEvent e,
        ITraceMetadataProvider meta,
        long start,
        long freq,
        int pid)
    {
        var tsUs = ToUs(e.Ts - start, freq);
        Resolve(meta, e.Id, out var name, out var cat);

        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteString("cat", cat);
        json.WriteString("ph", e.Phase == 0 ? "B" : "E");
        json.WriteNumber("ts", tsUs);
        json.WriteNumber("pid", pid);
        json.WriteNumber("tid", e.Tid);
        json.WriteEndObject();
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

    private readonly struct BeginEndEvent(int id, int tid, long ts, byte phase)
    {
        public readonly int Id = id;
        public readonly int Tid = tid;
        public readonly long Ts = ts;
        public readonly byte Phase = phase;
    }

    private readonly struct CompleteEvent(int id, int tid, long startTs, long dur, int depth, int parentId)
    {
        public readonly int Id = id;
        public readonly int Tid = tid;
        public readonly long StartTs = startTs;
        public readonly long Dur = dur;
        public readonly int Depth = depth;
        public readonly int ParentId = parentId;
    }


    private readonly struct Frame(int id, long start)
    {
        public readonly int Id = id;
        public readonly long Start = start;
    }
}
