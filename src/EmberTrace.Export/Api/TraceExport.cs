using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using EmberTrace.Export;
using EmberTrace.Metadata;
using EmberTrace.Sessions;
using static EmberTrace.Export.TraceTime;

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

            if (excludeMarkerBeginEnd && e.Id == MarkerId &&
                (e.Kind == TraceEventKind.Begin || e.Kind == TraceEventKind.End))
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

    private static void EnsureDir(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }
}

public static class TraceExport
{
    private const int StackCharLimit = 256;
    private const int MaxFileNameBytes = 255;
    private const int MaxUtf8BytesPerChar = 3;

    private static readonly SearchValues<char> InvalidFileNameChars =
        SearchValues.Create(Path.GetInvalidFileNameChars());

    public static void WriteChromeComplete(
        TraceSession session,
        Stream output,
        ITraceMetadataProvider? meta = null,
        bool sortByStartTimestamp = true,
        int pid = 1,
        string processName = "EmberTrace")
    {
        ChromeTraceExporter.WriteComplete(session, output, meta, sortByStartTimestamp, pid, processName);
    }

    public static void WriteChromeBeginEnd(
        TraceSession session,
        Stream output,
        ITraceMetadataProvider? meta = null,
        bool sortByTimestamp = true,
        int pid = 1,
        string processName = "EmberTrace")
    {
        ChromeTraceExporter.WriteBeginEnd(session, output, meta, sortByTimestamp, pid, processName);
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
        var r = await MarkedCompleteExAsync(name, outputPath, body, running, resumeOptions, pid, processName)
            .ConfigureAwait(false);
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

        var resume = RequireSliceable(running);
        var markerId = Tracer.Id(name);

        EnsureDir(outputPath);

        if (!resume)
            Tracer.Start();

        TraceSession session;
        Exception? error = null;
        try
        {
            using (Tracer.Scope(markerId))
            {
                body();
            }
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            session = Tracer.Stop();
        }

        var window = WriteSlice(session, outputPath, markerId, name, pid, processName, resume, resumeOptions,
            ref error);

        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();

        return new MarkedCompleteResult(name, markerId, outputPath, session, window.MinTs, window.MaxTs);
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

        var resume = RequireSliceable(running);
        var markerId = Tracer.Id(name);

        EnsureDir(outputPath);

        if (!resume)
            Tracer.Start();

        TraceSession session;
        Exception? error = null;
        try
        {
            await using (Tracer.ScopeAsync(markerId))
            {
                await body().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            session = Tracer.Stop();
        }

        var window = WriteSlice(session, outputPath, markerId, name, pid, processName, resume, resumeOptions,
            ref error);

        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();

        return new MarkedCompleteResult(name, markerId, outputPath, session, window.MinTs, window.MaxTs);
    }

    private static bool RequireSliceable(MarkedRunningSessionMode running)
    {
        if (!Tracer.IsRunning)
            return false;

        if (running == MarkedRunningSessionMode.ThrowIfRunning)
            throw new InvalidOperationException("Tracer session is already running.");

        return true;
    }

    private static (long MinTs, long MaxTs) WriteSlice(
        TraceSession session,
        string outputPath,
        int markerId,
        string name,
        int pid,
        string processName,
        bool resume,
        SessionOptions? resumeOptions,
        ref Exception? error)
    {
        var window = FindMarkerWindow(session, markerId);

        try
        {
            using var fs = File.Create(outputPath);
            var meta = CreateOverlayMeta(session.Metadata, markerId, name);
            WriteChromeCompleteSlice(session, fs, meta, window.MinTs, window.MaxTs, pid, processName, markerId, name);
        }
        catch (Exception ex)
        {
            error ??= ex;
        }
        finally
        {
            if (resume)
                Tracer.Start(resumeOptions ?? session.Options);
        }

        return window;
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

    private static string MakeNameFromCaller(string? caller, string? tag)
    {
        var baseName = string.IsNullOrWhiteSpace(caller) ? "Marked" : caller;
        if (string.IsNullOrWhiteSpace(tag))
            return baseName;

        return $"{baseName}_{SanitizeTag(tag)}";
    }

    private static string SanitizeTag(string tag)
    {
        return MapChars(tag, static c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
    }

    private static ITraceMetadataProvider CreateOverlayMeta(ITraceMetadataProvider baseMeta, int markerId, string name)
    {
        return new OverlayTraceMetadataProvider(baseMeta, markerId, name);
    }

    private static (long MinTs, long MaxTs) FindMarkerWindow(TraceSession session, int markerId)
    {
        var min = long.MaxValue;
        var max = long.MinValue;

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
                if (e.Timestamp > max)
                    max = e.Timestamp;
        }

        if (min == long.MaxValue || max == long.MinValue || max < min)
            return (session.StartTimestamp, session.EndTimestamp);

        return (min, max);
    }

    private static void WriteChromeCompleteSlice(
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

        var events = new List<TraceEventRecord>(4096);
        foreach (var e in session.EnumerateEvents())
        {
            if (e.Timestamp < minTs || e.Timestamp > maxTs)
                continue;

            if (e.Id == markerId && (e.Kind == TraceEventKind.Begin || e.Kind == TraceEventKind.End))
                continue;

            events.Add(e);
        }

        events.Sort(static (a, b) =>
            CompareEventOrder(a.Timestamp, a.TrackId, a.Sequence, b.Timestamp, b.TrackId, b.Sequence));

        var threadByTrack = new Dictionary<int, int>();
        for (var i = 0; i < events.Count; i++)
            threadByTrack.TryAdd(events[i].TrackId, events[i].ThreadId);

        foreach (var track in threadByTrack)
            WriteThreadName(json, pid, track.Key, ResolveThreadName(session, track.Value));

        WriteSyntheticTopLevel(json, pid, minTs, maxTs, freq, markerId, markerName);

        var flows = CollectFlows(events);
        flows.Sort(static (a, b) => CompareEventOrder(a.Timestamp, a.Tid, a.Sequence, b.Timestamp, b.Tid, b.Sequence));

        var markers = CollectInstantCounters(events);
        markers.Sort(static (a, b) =>
            CompareEventOrder(a.Timestamp, a.TrackId, a.Sequence, b.Timestamp, b.TrackId, b.Sequence));

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

        var complete = new List<CompleteSpan>(events.Count / 2);
        var asyncSpans = new List<AsyncSpan>();
        ScopeCollector.CollectComplete(new ScopeReader(events, maxTs), minTs, complete, asyncSpans);

        complete.Sort(static (a, b) =>
            CompareEventOrder(a.StartTs, a.TrackId, a.Sequence, b.StartTs, b.TrackId, b.Sequence));
        asyncSpans.Sort(static (a, b) =>
            CompareEventOrder(a.StartTs, a.StartTrackId, a.Sequence, b.StartTs, b.StartTrackId, b.Sequence));

        for (var i = 0; i < asyncSpans.Count; i++)
        {
            var span = asyncSpans[i];
            WriteAsyncPhase(json, span.Id, span.AsyncScopeId, span.StartTrackId, span.StartTs, meta, minTs, freq, pid,
                "b");
            WriteAsyncPhase(json, span.Id, span.AsyncScopeId, span.EndTrackId, span.EndTs, meta, minTs, freq, pid, "e");
        }

        for (var i = 0; i < complete.Count; i++)
            WriteCompleteEvent(json, complete[i], meta, minTs, freq, pid);

        json.WriteEndArray();
        json.WriteEndObject();
        json.Flush();
    }

    private static List<FlowEv> CollectFlows(List<TraceEventRecord> events)
    {
        var list = new List<FlowEv>();

        for (var i = 0; i < events.Count; i++)
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

            list.Add(new FlowEv(e.Id, e.TrackId, e.Timestamp, e.Sequence, e.FlowId, ph));
        }

        return list;
    }

    private static List<TraceEventRecord> CollectInstantCounters(List<TraceEventRecord> events)
    {
        var list = new List<TraceEventRecord>();
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            if (e.Kind == TraceEventKind.Instant || e.Kind == TraceEventKind.Counter)
                list.Add(e);
        }

        return list;
    }

    private static void WriteSyntheticTopLevel(
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

    private static void WriteCompleteEvent(
        Utf8JsonWriter json,
        in CompleteSpan e,
        ITraceMetadataProvider meta,
        long baseTs,
        long freq,
        int pid)
    {
        var tsUs = ToUs(e.StartTs - baseTs, freq);
        var durUs = ToUs(e.DurTicks, freq);
        Resolve(meta, e.Id, out var name, out var cat);

        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteString("cat", cat);
        json.WriteString("ph", "X");
        json.WriteNumber("ts", tsUs);
        json.WriteNumber("dur", durUs);
        json.WriteNumber("pid", pid);
        json.WriteNumber("tid", e.TrackId);
        json.WritePropertyName("args");
        json.WriteStartObject();
        json.WriteNumber("id", e.Id);
        json.WriteNumber("depth", e.Depth);
        if (e.ParentId != 0)
            json.WriteNumber("parent", e.ParentId);
        json.WriteEndObject();
        json.WriteEndObject();
    }

    private static void WriteAsyncPhase(
        Utf8JsonWriter json,
        int id,
        long asyncScopeId,
        int trackId,
        long timestamp,
        ITraceMetadataProvider meta,
        long baseTs,
        long freq,
        int pid,
        string phase)
    {
        Resolve(meta, id, out var name, out var cat);

        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteString("cat", cat);
        json.WriteString("ph", phase);
        json.WriteNumber("ts", ToUs(timestamp - baseTs, freq));
        json.WriteNumber("pid", pid);
        json.WriteNumber("tid", trackId);
        json.WriteNumber("id", asyncScopeId);
        json.WriteEndObject();
    }

    private static void WriteFlowEvent(
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

    private static void WriteInstantEvent(
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
        json.WriteNumber("tid", e.TrackId);
        json.WriteString("s", "t");
        json.WriteEndObject();
    }

    private static void WriteCounterEvent(
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
        json.WriteNumber("tid", e.TrackId);
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

    private static string ResolveThreadName(TraceSession session, int threadId)
    {
        if (session.ThreadNames.TryGetValue(threadId, out var name) && !string.IsNullOrWhiteSpace(name))
            return name;

        return $"Thread {threadId}";
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

    private static void EnsureDir(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    private static string DefaultTracePath(string name)
    {
        var suffix = $"_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
        var safe = TruncateUtf8(SafeFileName(name), MaxFileNameBytes - suffix.Length);
        return Path.Combine("traces", safe + suffix);
    }

    private static string SafeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "trace";

        return MapChars(name, static c => InvalidFileNameChars.Contains(c) || c == ' ' ? '_' : c);
    }

    private static string MapChars(string value, Func<char, char> map)
    {
        char[]? rented = null;
        var buffer = value.Length <= StackCharLimit
            ? stackalloc char[StackCharLimit]
            : rented = ArrayPool<char>.Shared.Rent(value.Length);

        try
        {
            for (var i = 0; i < value.Length; i++)
                buffer[i] = map(value[i]);

            return new string(buffer[..value.Length]);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);
        }
    }

    private static string TruncateUtf8(string value, int maxBytes)
    {
        if (maxBytes <= 0)
            return string.Empty;

        if (value.Length <= maxBytes / MaxUtf8BytesPerChar)
            return value;

        Span<byte> bytes = stackalloc byte[maxBytes];
        Encoding.UTF8.GetEncoder().Convert(value, bytes, true, out var charsUsed, out _, out _);
        return charsUsed >= value.Length ? value : value[..charsUsed];
    }

    private static int CompareEventOrder(long timestamp, int trackId, long sequence, long otherTimestamp,
        int otherTrackId, long otherSequence)
    {
        var cmp = timestamp.CompareTo(otherTimestamp);
        if (cmp != 0) return cmp;
        cmp = trackId.CompareTo(otherTrackId);
        if (cmp != 0) return cmp;
        return sequence.CompareTo(otherSequence);
    }

    private sealed class OverlayTraceMetadataProvider : ITraceMetadataProvider
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

    private readonly struct FlowEv
    {
        public readonly int Id;
        public readonly int Tid;
        public readonly long Timestamp;
        public readonly long Sequence;
        public readonly long FlowId;
        public readonly string Phase;

        public FlowEv(int id, int tid, long timestamp, long sequence, long flowId, string phase)
        {
            Id = id;
            Tid = tid;
            Timestamp = timestamp;
            Sequence = sequence;
            FlowId = flowId;
            Phase = phase;
        }
    }
}