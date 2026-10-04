using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using EmberTrace.Export;
using EmberTrace.Metadata;
using EmberTrace.Sessions;
using EmberTrace.Tracing;
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
    public TracingSession? Session { get; init; }
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
        return MarkedCompleteCore(resolved.Name, resolved.Path, body, options);
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
        return MarkedCompleteCoreAsync(resolved.Name, resolved.Path, body, options);
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

    private static MarkedCompleteResult MarkedCompleteCore(
        string name,
        string outputPath,
        Action body,
        MarkedCompleteOptions options)
    {
        var target = new MarkedTarget(options.Session);
        var attached = RequireSliceable(target, options.Running);
        var markerId = Tracer.Id(name);

        TraceFileNaming.EnsureDirectory(outputPath);

        if (!attached)
            target.Start();

        var started = Stopwatch.GetTimestamp();
        Exception? error = null;
        try
        {
            using (target.Scope(markerId))
            {
                body();
            }
        }
        catch (Exception ex)
        {
            error = ex;
        }

        return Finish(target.Capture(attached, started), name, outputPath, markerId, options, error);
    }

    private static async Task<MarkedCompleteResult> MarkedCompleteCoreAsync(
        string name,
        string outputPath,
        Func<Task> body,
        MarkedCompleteOptions options)
    {
        var target = new MarkedTarget(options.Session);
        var attached = RequireSliceable(target, options.Running);
        var markerId = Tracer.Id(name);

        TraceFileNaming.EnsureDirectory(outputPath);

        if (!attached)
            target.Start();

        var started = Stopwatch.GetTimestamp();
        Exception? error = null;
        try
        {
            await using (target.ScopeAsync(markerId))
            {
                await body().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            error = ex;
        }

        return Finish(target.Capture(attached, started), name, outputPath, markerId, options, error);
    }

    private static bool RequireSliceable(MarkedTarget target, MarkedRunningSessionMode running)
    {
        if (!target.IsRunning)
            return false;

        if (running == MarkedRunningSessionMode.ThrowIfRunning)
            throw new InvalidOperationException("Tracer session is already running.");

        return true;
    }

    private static MarkedCompleteResult Finish(
        TraceSession session,
        string name,
        string outputPath,
        int markerId,
        MarkedCompleteOptions options,
        Exception? error)
    {
        var window = FindMarkerWindow(session, markerId);

        try
        {
            using var fs = File.Create(outputPath);
            var meta = CreateOverlayMeta(session.Metadata, markerId, name);
            WriteChromeCompleteSlice(session, fs, meta, window.MinTs, window.MaxTs, Pid(options),
                ProcessName(options), markerId, name);
        }
        catch (Exception ex)
        {
            error ??= ex;
        }

        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();

        return new MarkedCompleteResult(name, markerId, outputPath, session, window.MinTs, window.MaxTs);
    }

    private readonly struct MarkedTarget(TracingSession? session)
    {
        private static readonly TimeSpan SliceMargin = TimeSpan.FromSeconds(1);

        public bool IsRunning => session?.IsRunning ?? Tracer.IsRunning;

        public void Start()
        {
            if (session is null)
                Tracer.Start();
            else
                session.Start();
        }

        public Scope Scope(int id)
        {
            return session is null ? Tracer.Scope(id) : session.Scope(id);
        }

        public AsyncScope ScopeAsync(int id)
        {
            return session is null ? Tracer.ScopeAsync(id) : session.ScopeAsync(id);
        }

        public TraceSession Capture(bool attached, long started)
        {
            if (!attached)
                return session is null ? Tracer.Stop() : session.Stop();

            var window = Stopwatch.GetElapsedTime(started) + SliceMargin;
            return session is null ? Tracer.Snapshot(window) : session.Snapshot(window);
        }
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

        var complete = new List<CompleteSpan>(events.Count / 2);
        var asyncSpans = new List<AsyncSpan>();
        ScopeCollector.CollectComplete(new ScopeReader(events, maxTs), minTs, complete, asyncSpans);

        complete.Sort(static (a, b) =>
            CompareEventOrder(a.StartTs, a.TrackId, a.Sequence, b.StartTs, b.TrackId, b.Sequence));
        asyncSpans.Sort(static (a, b) =>
            CompareEventOrder(a.StartTs, a.StartTrackId, a.Sequence, b.StartTs, b.StartTrackId, b.Sequence));

        for (var i = 0; i < asyncSpans.Count; i++)
            WriteAsyncSpan(json, asyncSpans[i], meta, minTs, freq, pid);

        for (var i = 0; i < complete.Count; i++)
            WriteCompleteEvent(json, complete[i], meta, minTs, freq, pid, ChromeEventArgsMode.Detailed);

        ChromeTraceExporter.WriteMarkers(json, events.FindAll(IsMarker), complete, meta, minTs, freq, pid);

        json.WriteEndArray();
        json.WriteEndObject();
        json.Flush();
    }

    private static bool IsMarker(TraceEventRecord e)
    {
        return e.Kind switch
        {
            TraceEventKind.Instant or TraceEventKind.Counter => true,
            TraceEventKind.FlowStart or TraceEventKind.FlowStep or TraceEventKind.FlowEnd => e.FlowId != 0,
            _ => false
        };
    }
}