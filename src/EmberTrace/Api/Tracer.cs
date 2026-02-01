using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EmberTrace.Flow;
using EmberTrace.Metadata;
using EmberTrace.Sessions;
using EmberTrace.Tracing;

namespace EmberTrace;

public static class Tracer
{
    private static readonly ConcurrentDictionary<int, string> IdToName = new();
    private static readonly ConcurrentDictionary<string, int> NameToId = new(StringComparer.Ordinal);

#if DEBUG
    private static int _idCollisionMode = (int)TracerIdCollisionMode.Warn;
#else
    private static int _idCollisionMode = (int)TracerIdCollisionMode.Ignore;
#endif
    public static bool IsRunning => Profiler.IsRunning;

    public static void Start(SessionOptions? options = null) => Profiler.Start(options);

    public static TraceSession Stop() => Profiler.Stop();

    public static Scope Scope(int id) => Profiler.Scope(id);

    public static AsyncScope ScopeAsync(int id) => new AsyncScope(id);

    public static long NewFlowId() => Profiler.NewFlowId();

    public static long FlowStartNew(int id) => Profiler.FlowStartNew(id);

    public static FlowScope Flow(int id) => Profiler.Flow(id);

    public static void FlowStart(int id, long flowId) => Profiler.FlowStart(id, flowId);

    public static void FlowStep(int id, long flowId) => Profiler.FlowStep(id, flowId);

    public static void FlowEnd(int id, long flowId) => Profiler.FlowEnd(id, flowId);

    public static void Instant(int id) => Profiler.Instant(id);

    public static void Counter(int id, long value) => Profiler.Counter(id, value);

    public static FlowHandle FlowStartNewHandle(int id) => Profiler.FlowStartNewHandle(id);

    public static void FlowEnd(FlowHandle handle) => handle.End();

    public static void FlowStep(FlowHandle handle) => handle.Step();

    public static ITraceMetadataProvider CreateMetadata() => TraceMetadata.CreateDefault();

    public static TracerIdCollisionMode IdCollisionMode
    {
        get => (TracerIdCollisionMode)Volatile.Read(ref _idCollisionMode);
        set => Volatile.Write(ref _idCollisionMode, (int)value);
    }

    public static int Id(string name)
    {
        var id = StableId(name);
        RegisterIdCollision(name, id);
        return id;
    }

    internal static int StableId(string name)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));

        unchecked
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;

            uint h = offset;
            foreach (var t in name)
            {
                h ^= t;
                h *= prime;
            }

            h &= 0x7fffffff;
            if (h == 0) h = 1;
            return (int)h;
        }
    }

    private static void RegisterIdCollision(string name, int id)
    {
        var mode = (TracerIdCollisionMode)Volatile.Read(ref _idCollisionMode);
#if !DEBUG
        if (mode == TracerIdCollisionMode.Ignore)
            return;
#endif

        NameToId.TryAdd(name, id);

        if (IdToName.TryGetValue(id, out var existing))
        {
            if (!string.Equals(existing, name, StringComparison.Ordinal))
                HandleIdCollision(mode, id, existing, name);
            return;
        }

        if (!IdToName.TryAdd(id, name))
        {
            if (IdToName.TryGetValue(id, out existing) && !string.Equals(existing, name, StringComparison.Ordinal))
                HandleIdCollision(mode, id, existing, name);
        }
    }

    private static void HandleIdCollision(TracerIdCollisionMode mode, int id, string existingName, string newName)
    {
        if (mode == TracerIdCollisionMode.Throw)
            throw new InvalidOperationException($"Tracer.Id collision: '{existingName}' and '{newName}' map to {id}.");

        if (mode == TracerIdCollisionMode.Warn)
            Trace.TraceWarning($"Tracer.Id collision: '{existingName}' and '{newName}' map to {id}.");
    }
}

public enum TracerIdCollisionMode
{
    Ignore = 0,
    Warn = 1,
    Throw = 2
}

public readonly struct AsyncScope : IAsyncDisposable
{
    private readonly int _id;
    private readonly bool _active;

    public AsyncScope(int id)
    {
        _id = id;
        _active = Tracer.IsRunning;
        if (_active)
            Profiler.Scope(id);
    }

    public ValueTask DisposeAsync()
    {
        if (_active)
            Profiler.End(_id);

        return ValueTask.CompletedTask;
    }
}
