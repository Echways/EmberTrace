using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using EmberTrace.Export;
using EmberTrace.Metadata;
using EmberTrace.Sessions;
using static EmberTrace.Export.ChromeJsonWriter;

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
        ArgumentNullException.ThrowIfNull(outputPath);

        TraceFileNaming.EnsureDirectory(outputPath);
        using var fs = File.Create(outputPath);
        TraceExport.WriteChromeComplete(CapturedSession, fs, meta, sortByStartTimestamp, pid, processName);
    }

}

public readonly struct MarkedCompleteOptions
{
    public string? OutputPath { get; init; }
    public bool Unique { get; init; }
    public MarkedRunningSessionMode Running { get; init; }
    public SessionOptions? ResumeOptions { get; init; }
    public int Pid { get; init; }
    public string? ProcessName { get; init; }
}

public static class TraceExport
{
    public static MarkedCompleteResult MarkedComplete(
        string name,
        Action body,
        MarkedCompleteOptions options,
        [CallerLineNumber] int line = 0)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(body);

        var resolved = ResolveTarget(name, options, line);
        return MarkedCompleteCore(resolved.Name, resolved.Path, body, options.Running, options.ResumeOptions,
            Pid(options), ProcessName(options));
    }

    public static Task<MarkedCompleteResult> MarkedCompleteAsync(
        string name,
        Func<Task> body,
        MarkedCompleteOptions options,
        [CallerLineNumber] int line = 0)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(body);

        var resolved = ResolveTarget(name, options, line);
        return MarkedCompleteCoreAsync(resolved.Name, resolved.Path, body, options.Running, options.ResumeOptions,
            Pid(options), ProcessName(options));
    }

    private static (string Name, string Path) ResolveTarget(string name, MarkedCompleteOptions options, int line)
    {
        var effective = options.Unique ? $"{name}_L{line}" : name;
        var path = string.IsNullOrWhiteSpace(options.OutputPath)
            ? TraceFileNaming.DefaultTracePath(effective)
            : options.OutputPath;

        return (effective, path);
    }

    private static int Pid(MarkedCompleteOptions options)
    {
        return options.Pid == 0 ? 1 : options.Pid;
    }

    private static string ProcessName(MarkedCompleteOptions options)
    {
        return options.ProcessName ?? "EmberTrace";
    }

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

    [Obsolete("Use MarkedComplete(name, body, options) instead.")]
    public static TraceSession MarkedComplete(
        string name,
        string outputPath,
        Action body,
        MarkedRunningSessionMode running = MarkedRunningSessionMode.ThrowIfRunning,
        SessionOptions? resumeOptions = null,
        int pid = 1,
        string processName = "EmberTrace")
    {
        var r = MarkedCompleteCore(name, outputPath, body, running, resumeOptions, pid, processName);
        return r.CapturedSession;
    }

    [Obsolete("Use MarkedComplete(name, body, options) instead.")]
    public static async Task<TraceSession> MarkedCompleteAsync(
        string name,
        string outputPath,
        Func<Task> body,
        MarkedRunningSessionMode running = MarkedRunningSessionMode.ThrowIfRunning,
        SessionOptions? resumeOptions = null,
        int pid = 1,
        string processName = "EmberTrace")
    {
        var r = await MarkedCompleteCoreAsync(name, outputPath, body, running, resumeOptions, pid, processName)
            .ConfigureAwait(false);
        return r.CapturedSession;
    }

    [Obsolete("Use MarkedComplete(name, body, options) instead.")]
    public static TraceSession MarkedComplete(string name, Action body)
    {
        var path = TraceFileNaming.DefaultTracePath(name);
        return MarkedCompleteCore(name, path, body, MarkedRunningSessionMode.ThrowIfRunning, null, 1, "EmberTrace")
            .CapturedSession;
    }

    [Obsolete("Use MarkedComplete(name, body, options) instead.")]
    public static async Task<TraceSession> MarkedCompleteAsync(string name, Func<Task> body)
    {
        var path = TraceFileNaming.DefaultTracePath(name);
        var r = await MarkedCompleteCoreAsync(name, path, body, MarkedRunningSessionMode.ThrowIfRunning, null, 1,
            "EmberTrace").ConfigureAwait(false);
        return r.CapturedSession;
    }

    [Obsolete("Use MarkedComplete(name, body, options) instead.")]
    public static MarkedCompleteResult MarkedCompleteEx(
        string name,
        Action body,
        MarkedRunningSessionMode running = MarkedRunningSessionMode.ThrowIfRunning,
        SessionOptions? resumeOptions = null,
        int pid = 1,
        string processName = "EmberTrace")
    {
        var path = TraceFileNaming.DefaultTracePath(name);
        return MarkedCompleteCore(name, path, body, running, resumeOptions, pid, processName);
    }

    [Obsolete("Use MarkedComplete(name, body, options) instead.")]
    public static Task<MarkedCompleteResult> MarkedCompleteExAsync(
        string name,
        Func<Task> body,
        MarkedRunningSessionMode running = MarkedRunningSessionMode.ThrowIfRunning,
        SessionOptions? resumeOptions = null,
        int pid = 1,
        string processName = "EmberTrace")
    {
        var path = TraceFileNaming.DefaultTracePath(name);
        return MarkedCompleteCoreAsync(name, path, body, running, resumeOptions, pid, processName);
    }

    [Obsolete("Use MarkedComplete(name, body, options) instead.")]
    public static MarkedCompleteResult MarkedCompleteEx(
        string name,
        string outputPath,
        Action body,
        MarkedRunningSessionMode running = MarkedRunningSessionMode.ThrowIfRunning,
        SessionOptions? resumeOptions = null,
        int pid = 1,
        string processName = "EmberTrace")
    {
        return MarkedCompleteCore(name, outputPath, body, running, resumeOptions, pid, processName);
    }

    [Obsolete("Use MarkedComplete(name, body, options) instead.")]
    public static Task<MarkedCompleteResult> MarkedCompleteExAsync(
        string name,
        string outputPath,
        Func<Task> body,
        MarkedRunningSessionMode running = MarkedRunningSessionMode.ThrowIfRunning,
        SessionOptions? resumeOptions = null,
        int pid = 1,
        string processName = "EmberTrace")
    {
        return MarkedCompleteCoreAsync(name, outputPath, body, running, resumeOptions, pid, processName);
    }

    private static MarkedCompleteResult MarkedCompleteCore(
        string name,
        string outputPath,
        Action body,
        MarkedRunningSessionMode running,
        SessionOptions? resumeOptions,
        int pid,
        string processName)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(body);

        var resume = RequireSliceable(running);
        var markerId = Tracer.Id(name);

        TraceFileNaming.EnsureDirectory(outputPath);

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

    private static async Task<MarkedCompleteResult> MarkedCompleteCoreAsync(
        string name,
        string outputPath,
        Func<Task> body,
        MarkedRunningSessionMode running,
        SessionOptions? resumeOptions,
        int pid,
        string processName)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(body);

        var resume = RequireSliceable(running);
        var markerId = Tracer.Id(name);

        TraceFileNaming.EnsureDirectory(outputPath);

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

    [Obsolete("Use MarkedComplete(name, body, options) instead.")]
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
        var name = TraceFileNaming.MakeNameFromCaller(caller, tag);
        var path = string.IsNullOrWhiteSpace(outputPath) ? TraceFileNaming.DefaultTracePath(name) : outputPath;
        return MarkedCompleteCore(name, path, body, running, resumeOptions, pid, processName);
    }

    [Obsolete("Use MarkedComplete(name, body, options) instead.")]
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
        var name = TraceFileNaming.MakeNameFromCaller(caller, tag);
        var path = string.IsNullOrWhiteSpace(outputPath) ? TraceFileNaming.DefaultTracePath(name) : outputPath;
        return MarkedCompleteCoreAsync(name, path, body, running, resumeOptions, pid, processName);
    }

    [Obsolete("Use MarkedComplete(name, body, options) instead.")]
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
        var name = TraceFileNaming.MakeNameFromCaller(caller, tag);
        var path = string.IsNullOrWhiteSpace(outputPath) ? TraceFileNaming.DefaultTracePath(name) : outputPath;
        return MarkedCompleteCore(name, path, body, running, resumeOptions, pid, processName)
            .CapturedSession;
    }

    [Obsolete("Use MarkedComplete(name, body, options) instead.")]
    public static async Task<TraceSession> MarkedCompleteAsync(
        Func<Task> body,
        string? tag = null,
        MarkedRunningSessionMode running = MarkedRunningSessionMode.ThrowIfRunning,
        SessionOptions? resumeOptions = null,
        int pid = 1,
        string processName = "EmberTrace",
        string? outputPath = null,
        [CallerMemberName] string? caller = null)
    {
        var name = TraceFileNaming.MakeNameFromCaller(caller, tag);
        var path = string.IsNullOrWhiteSpace(outputPath) ? TraceFileNaming.DefaultTracePath(name) : outputPath;
        var r = await MarkedCompleteCoreAsync(name, path, body, running, resumeOptions, pid, processName)
            .ConfigureAwait(false);
        return r.CapturedSession;
    }

    [Obsolete("Use MarkedComplete(name, body, options) instead.")]
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
        var baseName = TraceFileNaming.MakeNameFromCaller(caller, tag);
        var name = $"{baseName}_L{line}";
        var path = string.IsNullOrWhiteSpace(outputPath) ? TraceFileNaming.DefaultTracePath(name) : outputPath;
        return MarkedCompleteCore(name, path, body, running, resumeOptions, pid, processName);
    }

    [Obsolete("Use MarkedComplete(name, body, options) instead.")]
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
        var baseName = TraceFileNaming.MakeNameFromCaller(caller, tag);
        var name = $"{baseName}_L{line}";
        var path = string.IsNullOrWhiteSpace(outputPath) ? TraceFileNaming.DefaultTracePath(name) : outputPath;
        return MarkedCompleteCoreAsync(name, path, body, running, resumeOptions, pid, processName);
    }

    [Obsolete("Use MarkedComplete(name, body, options) instead.")]
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
        var baseName = TraceFileNaming.MakeNameFromCaller(caller, tag);
        var name = $"{baseName}_L{line}";
        var path = string.IsNullOrWhiteSpace(outputPath) ? TraceFileNaming.DefaultTracePath(name) : outputPath;
        return MarkedCompleteCore(name, path, body, running, resumeOptions, pid, processName)
            .CapturedSession;
    }

    [Obsolete("Use MarkedComplete(name, body, options) instead.")]
    public static async Task<TraceSession> MarkedCompleteUniqueAsync(
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
        var baseName = TraceFileNaming.MakeNameFromCaller(caller, tag);
        var name = $"{baseName}_L{line}";
        var path = string.IsNullOrWhiteSpace(outputPath) ? TraceFileNaming.DefaultTracePath(name) : outputPath;
        var r = await MarkedCompleteCoreAsync(name, path, body, running, resumeOptions, pid, processName)
            .ConfigureAwait(false);
        return r.CapturedSession;
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
                var f = flows[fi];
                WriteFlowEvent(json, f.Id, f.Tid, f.Timestamp, f.FlowId, f.Phase, meta, minTs, freq, pid,
                    ChromeEventArgsMode.Detailed);
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
            WriteCompleteEvent(json, complete[i], meta, minTs, freq, pid, ChromeEventArgsMode.Detailed);

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