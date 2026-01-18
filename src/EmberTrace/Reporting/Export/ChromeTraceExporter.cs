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
        int pid = 1)
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
        int pid = 1)
    {
        using var ms = new MemoryStream(capacity: 256 * 1024);
        WriteComplete(session, ms, meta, sortByStartTimestamp, pid);
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

                stack.RemoveAt(stack.Count - 1);

                var dur = e.Timestamp - top.Start;
                if (dur <= 0)
                    continue;

                list.Add(new CompleteEvent(e.Id, e.ThreadId, top.Start, dur));
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

    private readonly struct BeginEndEvent
    {
        public readonly int Id;
        public readonly int Tid;
        public readonly long Ts;
        public readonly byte Phase;

        public BeginEndEvent(int id, int tid, long ts, byte phase)
        {
            Id = id;
            Tid = tid;
            Ts = ts;
            Phase = phase;
        }
    }

    private readonly struct CompleteEvent
    {
        public readonly int Id;
        public readonly int Tid;
        public readonly long StartTs;
        public readonly long Dur;

        public CompleteEvent(int id, int tid, long startTs, long dur)
        {
            Id = id;
            Tid = tid;
            StartTs = startTs;
            Dur = dur;
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
