using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace;

public enum MarkedRunningSessionMode
{
    ThrowIfRunning = 0,
    SliceAndResume = 1
}

public readonly struct MarkedCompleteResult
{
    public readonly string Name;
    public readonly int MarkerId;
    public readonly string SlicePath;
    public readonly TraceSession CapturedSession;
    public readonly long WindowMinTimestamp;
    public readonly long WindowMaxTimestamp;

    public bool HasWindow => WindowMaxTimestamp >= WindowMinTimestamp;

    internal MarkedCompleteResult(
        string name,
        int markerId,
        string slicePath,
        TraceSession capturedSession,
        long windowMinTimestamp,
        long windowMaxTimestamp)
    {
        Name = name;
        MarkerId = markerId;
        SlicePath = slicePath;
        CapturedSession = capturedSession;
        WindowMinTimestamp = windowMinTimestamp;
        WindowMaxTimestamp = windowMaxTimestamp;
    }

    public IEnumerable<TraceEventRecord> EnumerateSliceEvents(bool excludeMarkerBeginEnd = true)
    {
        foreach (var e in CapturedSession.EnumerateEvents())
        {
            if (e.Timestamp < WindowMinTimestamp || e.Timestamp > WindowMaxTimestamp)
                continue;

            if (excludeMarkerBeginEnd && e.Id == MarkerId && (e.Kind == TraceEventKind.Begin || e.Kind == TraceEventKind.End))
                continue;

            yield return e;
        }
    }

    public void SaveFullChromeComplete(
        string outputPath,
        ITraceMetadataProvider? meta = null,
        bool sortByStartTimestamp = true,
        int pid = 1,
        string processName = "EmberTrace")
    {
        if (outputPath is null) throw new ArgumentNullException(nameof(outputPath));

        EnsureDir(outputPath);
        using var fs = File.Create(outputPath);
        TraceExport.WriteChromeComplete(CapturedSession, fs, meta, sortByStartTimestamp, pid, processName);
    }

    static void EnsureDir(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }
}

public static class TraceExport
{
    public static void WriteChromeComplete(
        TraceSession session,
        Stream output,
        ITraceMetadataProvider? meta = null,
        bool sortByStartTimestamp = true,
        int pid = 1,
        string processName = "EmberTrace")
    {
        Export.ChromeTraceExporter.WriteComplete(session, output, meta, sortByStartTimestamp, pid, processName);
    }

    public static void WriteChromeBeginEnd(
        TraceSession session,
        Stream output,
        ITraceMetadataProvider? meta = null,
        bool sortByTimestamp = true,
        int pid = 1,
        string processName = "EmberTrace")
    {
        Export.ChromeTraceExporter.WriteBeginEnd(session, output, meta, sortByTimestamp, pid, processName);
    }

    public static TraceSession MarkedComplete(
        string name,
        string outputPath,
        Action body,
        MarkedRunningSessionMode running = MarkedRunningSessionMode.ThrowIfRunning,
        SessionOptions? resumeOptions = null,
        int pid = 1,
        string processName = "EmberTrace")
    {
        var r = MarkedCompleteEx(name, outputPath, body, running, resumeOptions, pid, processName);
        return r.CapturedSession;
    }

    public static async Task<TraceSession> MarkedCompleteAsync(
        string name,
        string outputPath,
        Func<Task> body,
        MarkedRunningSessionMode running = MarkedRunningSessionMode.ThrowIfRunning,
        SessionOptions? resumeOptions = null,
        int pid = 1,
        string processName = "EmberTrace")
    {
        var r = await MarkedCompleteExAsync(name, outputPath, body, running, resumeOptions, pid, processName).ConfigureAwait(false);
        return r.CapturedSession;
    }

    public static TraceSession MarkedComplete(string name, Action body)
    {
        var path = DefaultTracePath(name);
        return MarkedComplete(name, path, body);
    }

    public static Task<TraceSession> MarkedCompleteAsync(string name, Func<Task> body)
    {
        var path = DefaultTracePath(name);
        return MarkedCompleteAsync(name, path, body);
    }

    public static MarkedCompleteResult MarkedCompleteEx(
        string name,
        Action body,
        MarkedRunningSessionMode running = MarkedRunningSessionMode.ThrowIfRunning,
        SessionOptions? resumeOptions = null,
        int pid = 1,
        string processName = "EmberTrace")
    {
        var path = DefaultTracePath(name);
        return MarkedCompleteEx(name, path, body, running, resumeOptions, pid, processName);
    }

    public static Task<MarkedCompleteResult> MarkedCompleteExAsync(
        string name,
        Func<Task> body,
        MarkedRunningSessionMode running = MarkedRunningSessionMode.ThrowIfRunning,
        SessionOptions? resumeOptions = null,
        int pid = 1,
        string processName = "EmberTrace")
    {
        var path = DefaultTracePath(name);
        return MarkedCompleteExAsync(name, path, body, running, resumeOptions, pid, processName);
    }

    public static MarkedCompleteResult MarkedCompleteEx(
        string name,
        string outputPath,
        Action body,
        MarkedRunningSessionMode running = MarkedRunningSessionMode.ThrowIfRunning,
        SessionOptions? resumeOptions = null,
        int pid = 1,
        string processName = "EmberTrace")
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        if (outputPath is null) throw new ArgumentNullException(nameof(outputPath));
        if (body is null) throw new ArgumentNullException(nameof(body));

        var markerId = Tracer.Id(name);
        var meta = CreateOverlayMeta(markerId, name);

        EnsureDir(outputPath);

        if (!Tracer.IsRunning)
        {
            TraceSession session;
            Exception? error = null;

            Tracer.Start();
            try
            {
                using (Tracer.Scope(markerId))
                    body();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                session = Tracer.Stop();
            }

            var w = FindMarkerWindow(session, markerId);
            using (var fs = File.Create(outputPath))
                WriteChromeCompleteSlice(session, fs, meta, w.MinTs, w.MaxTs, pid, processName, markerId, name);

            if (error is not null)
                throw error;

            return new MarkedCompleteResult(name, markerId, outputPath, session, w.MinTs, w.MaxTs);
        }

        if (running == MarkedRunningSessionMode.ThrowIfRunning)
            throw new InvalidOperationException("Tracer session is already running.");

        Exception? bodyError = null;
        try
        {
            using (Tracer.Scope(markerId))
                body();
        }
        catch (Exception ex)
        {
            bodyError = ex;
        }

        var stopped = Tracer.Stop();

        var window = FindMarkerWindow(stopped, markerId);
        using (var fs = File.Create(outputPath))
            WriteChromeCompleteSlice(stopped, fs, meta, window.MinTs, window.MaxTs, pid, processName, markerId, name);

        Tracer.Start(resumeOptions);

        if (bodyError is not null)
            throw bodyError;

        return new MarkedCompleteResult(name, markerId, outputPath, stopped, window.MinTs, window.MaxTs);
    }

    public static async Task<MarkedCompleteResult> MarkedCompleteExAsync(
        string name,
        string outputPath,
        Func<Task> body,
        MarkedRunningSessionMode running = MarkedRunningSessionMode.ThrowIfRunning,
        SessionOptions? resumeOptions = null,
        int pid = 1,
        string processName = "EmberTrace")
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        if (outputPath is null) throw new ArgumentNullException(nameof(outputPath));
        if (body is null) throw new ArgumentNullException(nameof(body));

        var markerId = Tracer.Id(name);
        var meta = CreateOverlayMeta(markerId, name);

        EnsureDir(outputPath);

        if (!Tracer.IsRunning)
        {
            TraceSession session;
            Exception? error = null;

            Tracer.Start();
            try
            {
                await using (Tracer.ScopeAsync(markerId))
                    await body().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                session = Tracer.Stop();
            }

            var w = FindMarkerWindow(session, markerId);
            using (var fs = File.Create(outputPath))
                WriteChromeCompleteSlice(session, fs, meta, w.MinTs, w.MaxTs, pid, processName, markerId, name);

            if (error is not null)
                throw error;

            return new MarkedCompleteResult(name, markerId, outputPath, session, w.MinTs, w.MaxTs);
        }

        if (running == MarkedRunningSessionMode.ThrowIfRunning)
            throw new InvalidOperationException("Tracer session is already running.");

        Exception? bodyError = null;
        try
        {
            await using (Tracer.ScopeAsync(markerId))
                await body().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            bodyError = ex;
        }

        var stopped = Tracer.Stop();

        var window = FindMarkerWindow(stopped, markerId);
        using (var fs = File.Create(outputPath))
            WriteChromeCompleteSlice(stopped, fs, meta, window.MinTs, window.MaxTs, pid, processName, markerId, name);

        Tracer.Start(resumeOptions);

        if (bodyError is not null)
            throw bodyError;

        return new MarkedCompleteResult(name, markerId, outputPath, stopped, window.MinTs, window.MaxTs);
    }

    public static MarkedCompleteResult MarkedCompleteEx(
        Action body,
        string? tag = null,
        MarkedRunningSessionMode running = MarkedRunningSessionMode.ThrowIfRunning,
        SessionOptions? resumeOptions = null,
        int pid = 1,
        string processName = "EmberTrace",
        string? outputPath = null,
        [CallerMemberName] string? caller = null)
    {
        var name = MakeNameFromCaller(caller, tag);
        var path = string.IsNullOrWhiteSpace(outputPath) ? DefaultTracePath(name) : outputPath;
        return MarkedCompleteEx(name, path, body, running, resumeOptions, pid, processName);
    }

    public static Task<MarkedCompleteResult> MarkedCompleteExAsync(
        Func<Task> body,
        string? tag = null,
        MarkedRunningSessionMode running = MarkedRunningSessionMode.ThrowIfRunning,
        SessionOptions? resumeOptions = null,
        int pid = 1,
        string processName = "EmberTrace",
        string? outputPath = null,
        [CallerMemberName] string? caller = null)
    {
        var name = MakeNameFromCaller(caller, tag);
        var path = string.IsNullOrWhiteSpace(outputPath) ? DefaultTracePath(name) : outputPath;
        return MarkedCompleteExAsync(name, path, body, running, resumeOptions, pid, processName);
    }

    public static TraceSession MarkedComplete(
        Action body,
        string? tag = null,
        MarkedRunningSessionMode running = MarkedRunningSessionMode.ThrowIfRunning,
        SessionOptions? resumeOptions = null,
        int pid = 1,
        string processName = "EmberTrace",
        string? outputPath = null,
        [CallerMemberName] string? caller = null)
    {
        var name = MakeNameFromCaller(caller, tag);
        var path = string.IsNullOrWhiteSpace(outputPath) ? DefaultTracePath(name) : outputPath;
        return MarkedComplete(name, path, body, running, resumeOptions, pid, processName);
    }

    public static Task<TraceSession> MarkedCompleteAsync(
        Func<Task> body,
        string? tag = null,
        MarkedRunningSessionMode running = MarkedRunningSessionMode.ThrowIfRunning,
        SessionOptions? resumeOptions = null,
        int pid = 1,
        string processName = "EmberTrace",
        string? outputPath = null,
        [CallerMemberName] string? caller = null)
    {
        var name = MakeNameFromCaller(caller, tag);
        var path = string.IsNullOrWhiteSpace(outputPath) ? DefaultTracePath(name) : outputPath;
        return MarkedCompleteAsync(name, path, body, running, resumeOptions, pid, processName);
    }

    public static MarkedCompleteResult MarkedCompleteExUnique(
        Action body,
        string? tag = null,
        MarkedRunningSessionMode running = MarkedRunningSessionMode.ThrowIfRunning,
        SessionOptions? resumeOptions = null,
        int pid = 1,
        string processName = "EmberTrace",
        string? outputPath = null,
        [CallerMemberName] string? caller = null,
        [CallerLineNumber] int line = 0)
    {
        var baseName = MakeNameFromCaller(caller, tag);
        var name = $"{baseName}_L{line}";
        var path = string.IsNullOrWhiteSpace(outputPath) ? DefaultTracePath(name) : outputPath;
        return MarkedCompleteEx(name, path, body, running, resumeOptions, pid, processName);
    }

    public static Task<MarkedCompleteResult> MarkedCompleteExUniqueAsync(
        Func<Task> body,
        string? tag = null,
        MarkedRunningSessionMode running = MarkedRunningSessionMode.ThrowIfRunning,
        SessionOptions? resumeOptions = null,
        int pid = 1,
        string processName = "EmberTrace",
        string? outputPath = null,
        [CallerMemberName] string? caller = null,
        [CallerLineNumber] int line = 0)
    {
        var baseName = MakeNameFromCaller(caller, tag);
        var name = $"{baseName}_L{line}";
        var path = string.IsNullOrWhiteSpace(outputPath) ? DefaultTracePath(name) : outputPath;
        return MarkedCompleteExAsync(name, path, body, running, resumeOptions, pid, processName);
    }

    public static TraceSession MarkedCompleteUnique(
        Action body,
        string? tag = null,
        MarkedRunningSessionMode running = MarkedRunningSessionMode.ThrowIfRunning,
        SessionOptions? resumeOptions = null,
        int pid = 1,
        string processName = "EmberTrace",
        string? outputPath = null,
        [CallerMemberName] string? caller = null,
        [CallerLineNumber] int line = 0)
    {
        var baseName = MakeNameFromCaller(caller, tag);
        var name = $"{baseName}_L{line}";
        var path = string.IsNullOrWhiteSpace(outputPath) ? DefaultTracePath(name) : outputPath;
        return MarkedComplete(name, path, body, running, resumeOptions, pid, processName);
    }

    public static Task<TraceSession> MarkedCompleteUniqueAsync(
        Func<Task> body,
        string? tag = null,
        MarkedRunningSessionMode running = MarkedRunningSessionMode.ThrowIfRunning,
        SessionOptions? resumeOptions = null,
        int pid = 1,
        string processName = "EmberTrace",
        string? outputPath = null,
        [CallerMemberName] string? caller = null,
        [CallerLineNumber] int line = 0)
    {
        var baseName = MakeNameFromCaller(caller, tag);
        var name = $"{baseName}_L{line}";
        var path = string.IsNullOrWhiteSpace(outputPath) ? DefaultTracePath(name) : outputPath;
        return MarkedCompleteAsync(name, path, body, running, resumeOptions, pid, processName);
    }

    static string MakeNameFromCaller(string? caller, string? tag)
    {
        var baseName = string.IsNullOrWhiteSpace(caller) ? "Marked" : caller;
        if (string.IsNullOrWhiteSpace(tag))
            return baseName;

        return $"{baseName}_{SanitizeTag(tag)}";
    }

    static string SanitizeTag(string tag)
    {
        Span<char> buf = stackalloc char[tag.Length];
        var n = 0;

        for (int i = 0; i < tag.Length; i++)
        {
            var c = tag[i];
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                buf[n++] = c;
            else
                buf[n++] = '_';
        }

        return new string(buf[..n]);
    }

    static ITraceMetadataProvider CreateOverlayMeta(int markerId, string name)
    {
        var baseMeta = TraceMetadata.CreateDefault();
        return new OverlayTraceMetadataProvider(baseMeta, markerId, name);
    }

    static (long MinTs, long MaxTs) FindMarkerWindow(TraceSession session, int markerId)
    {
        long min = long.MaxValue;
        long max = long.MinValue;

        foreach (var e in session.EnumerateEvents())
        {
            if (e.Id != markerId)
                continue;

            if (e.Kind == TraceEventKind.Begin)
            {
                if (e.Timestamp < min) min = e.Timestamp;
                continue;
            }

            if (e.Kind == TraceEventKind.End)
            {
                if (e.Timestamp > max) max = e.Timestamp;
                continue;
            }
        }

        if (min == long.MaxValue || max == long.MinValue || max < min)
            return (session.StartTimestamp, session.EndTimestamp);

        return (min, max);
    }

    static void WriteChromeCompleteSlice(
        TraceSession session,
        Stream output,
        ITraceMetadataProvider meta,
        long minTs,
        long maxTs,
        int pid,
        string processName,
        int markerId,
        string markerName)
    {
        var freq = session.TimestampFrequency;

        using var json = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = false });

        json.WriteStartObject();
        json.WriteString("displayTimeUnit", "ms");
        json.WritePropertyName("traceEvents");
        json.WriteStartArray();

        WriteProcessName(json, pid, processName);

        var events = new List<TraceEventRecord>(capacity: 4096);
        foreach (var e in session.EnumerateEvents())
        {
            if (e.Timestamp < minTs || e.Timestamp > maxTs)
                continue;

            if (e.Id == markerId && (e.Kind == TraceEventKind.Begin || e.Kind == TraceEventKind.End))
                continue;

            events.Add(e);
        }

        var tids = new HashSet<int>();
        for (int i = 0; i < events.Count; i++)
            tids.Add(events[i].ThreadId);

        foreach (var tid in tids)
            WriteThreadName(json, pid, tid, $"Thread {tid}");

        WriteSyntheticTopLevel(json, pid, minTs, maxTs, freq, markerId, markerName);

        var flows = CollectFlows(events);
        flows.Sort(static (a, b) => a.Timestamp.CompareTo(b.Timestamp));

        var markers = CollectInstantCounters(events);
        markers.Sort(static (a, b) => a.Timestamp.CompareTo(b.Timestamp));

        var fi = 0;
        var mi = 0;
        while (fi < flows.Count || mi < markers.Count)
        {
            if (mi >= markers.Count || (fi < flows.Count && flows[fi].Timestamp <= markers[mi].Timestamp))
            {
                WriteFlowEvent(json, flows[fi], meta, minTs, freq, pid);
                fi++;
                continue;
            }

            var m = markers[mi++];
            switch (m.Kind)
            {
                case TraceEventKind.Instant:
                    WriteInstantEvent(json, m, meta, minTs, freq, pid);
                    break;
                case TraceEventKind.Counter:
                    WriteCounterEvent(json, m, meta, minTs, freq, pid);
                    break;
            }
        }

        var complete = CollectComplete(events);
        complete.Sort(static (a, b) => a.StartTs.CompareTo(b.StartTs));
        for (int i = 0; i < complete.Count; i++)
            WriteCompleteEvent(json, complete[i], meta, minTs, freq, pid);

        json.WriteEndArray();
        json.WriteEndObject();
        json.Flush();
    }

    static List<FlowEv> CollectFlows(List<TraceEventRecord> events)
    {
        var list = new List<FlowEv>();

        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            if (e.FlowId == 0)
                continue;

            var ph = e.Kind switch
            {
                TraceEventKind.FlowStart => "s",
                TraceEventKind.FlowStep => "t",
                TraceEventKind.FlowEnd => "f",
                _ => null
            };

            if (ph is null)
                continue;

            list.Add(new FlowEv(e.Id, e.ThreadId, e.Timestamp, e.FlowId, ph));
        }

        return list;
    }

    static List<TraceEventRecord> CollectInstantCounters(List<TraceEventRecord> events)
    {
        var list = new List<TraceEventRecord>();
        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            if (e.Kind == TraceEventKind.Instant || e.Kind == TraceEventKind.Counter)
                list.Add(e);
        }

        return list;
    }

    static List<CompleteEv> CollectComplete(List<TraceEventRecord> events)
    {
        var stacks = new Dictionary<int, List<Frame>>(capacity: 8);
        var list = new List<CompleteEv>(capacity: events.Count / 2);

        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
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

            list.Add(new CompleteEv(e.Id, e.ThreadId, top.Start, dur, depth, parentId));
        }

        return list;
    }

    static void WriteSyntheticTopLevel(
        Utf8JsonWriter json,
        int pid,
        long minTs,
        long maxTs,
        long freq,
        int markerId,
        string markerName)
    {
        var durUs = ToUs(maxTs - minTs, freq);

        json.WriteStartObject();
        json.WriteString("name", markerName);
        json.WriteString("cat", "Marked");
        json.WriteString("ph", "X");
        json.WriteNumber("ts", 0);
        json.WriteNumber("dur", durUs);
        json.WriteNumber("pid", pid);
        json.WriteNumber("tid", 0);
        json.WritePropertyName("args");
        json.WriteStartObject();
        json.WriteNumber("id", markerId);
        json.WriteEndObject();
        json.WriteEndObject();
    }

    static void WriteCompleteEvent(
        Utf8JsonWriter json,
        in CompleteEv e,
        ITraceMetadataProvider meta,
        long baseTs,
        long freq,
        int pid)
    {
        var tsUs = ToUs(e.StartTs - baseTs, freq);
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

    static void WriteFlowEvent(
        Utf8JsonWriter json,
        in FlowEv e,
        ITraceMetadataProvider meta,
        long baseTs,
        long freq,
        int pid)
    {
        var tsUs = ToUs(e.Timestamp - baseTs, freq);
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

    static void WriteInstantEvent(
        Utf8JsonWriter json,
        in TraceEventRecord e,
        ITraceMetadataProvider meta,
        long baseTs,
        long freq,
        int pid)
    {
        var tsUs = ToUs(e.Timestamp - baseTs, freq);
        Resolve(meta, e.Id, out var name, out var cat);

        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteString("cat", cat);
        json.WriteString("ph", "i");
        json.WriteNumber("ts", tsUs);
        json.WriteNumber("pid", pid);
        json.WriteNumber("tid", e.ThreadId);
        json.WriteString("s", "t");
        json.WriteEndObject();
    }

    static void WriteCounterEvent(
        Utf8JsonWriter json,
        in TraceEventRecord e,
        ITraceMetadataProvider meta,
        long baseTs,
        long freq,
        int pid)
    {
        var tsUs = ToUs(e.Timestamp - baseTs, freq);
        Resolve(meta, e.Id, out var name, out var cat);

        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteString("cat", cat);
        json.WriteString("ph", "C");
        json.WriteNumber("ts", tsUs);
        json.WriteNumber("pid", pid);
        json.WriteNumber("tid", e.ThreadId);
        json.WritePropertyName("args");
        json.WriteStartObject();
        json.WriteNumber("value", e.Value);
        json.WriteEndObject();
        json.WriteEndObject();
    }

    static void WriteProcessName(Utf8JsonWriter json, int pid, string name)
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

    static void WriteThreadName(Utf8JsonWriter json, int pid, int tid, string name)
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

    static long ToUs(long deltaTicks, long freq)
    {
        if (deltaTicks <= 0)
            return 0;

        return (deltaTicks * 1_000_000L) / freq;
    }

    static void Resolve(ITraceMetadataProvider meta, int id, out string name, out string category)
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

    static void EnsureDir(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    static string DefaultTracePath(string name)
    {
        var safe = SafeFileName(name);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        return Path.Combine("traces", $"{safe}_{stamp}.json");
    }

    static string SafeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "trace";

        var invalid = Path.GetInvalidFileNameChars();
        var set = new HashSet<char>(invalid);

        Span<char> buffer = stackalloc char[name.Length];
        var n = 0;

        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (set.Contains(c) || c == ' ')
                c = '_';
            buffer[n++] = c;
        }

        return new string(buffer[..n]);
    }

    sealed class OverlayTraceMetadataProvider : ITraceMetadataProvider
    {
        private readonly ITraceMetadataProvider _base;
        private readonly int _id;
        private readonly TraceMeta _meta;

        public OverlayTraceMetadataProvider(ITraceMetadataProvider @base, int id, string name)
        {
            _base = @base;
            _id = id;
            _meta = new TraceMeta(id, name, "Marked");
        }

        public bool TryGet(int id, out TraceMeta metadata)
        {
            if (id == _id)
            {
                metadata = _meta;
                return true;
            }

            return _base.TryGet(id, out metadata);
        }
    }

    readonly struct Frame
    {
        public readonly int Id;
        public readonly long Start;

        public Frame(int id, long start)
        {
            Id = id;
            Start = start;
        }
    }

    readonly struct CompleteEv
    {
        public readonly int Id;
        public readonly int Tid;
        public readonly long StartTs;
        public readonly long Dur;
        public readonly int Depth;
        public readonly int ParentId;

        public CompleteEv(int id, int tid, long startTs, long dur, int depth, int parentId)
        {
            Id = id;
            Tid = tid;
            StartTs = startTs;
            Dur = dur;
            Depth = depth;
            ParentId = parentId;
        }
    }

    readonly struct FlowEv
    {
        public readonly int Id;
        public readonly int Tid;
        public readonly long Timestamp;
        public readonly long FlowId;
        public readonly string Phase;

        public FlowEv(int id, int tid, long timestamp, long flowId, string phase)
        {
            Id = id;
            Tid = tid;
            Timestamp = timestamp;
            FlowId = flowId;
            Phase = phase;
        }
    }
}
