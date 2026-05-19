using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using EmberTrace.Flow;
using EmberTrace.Metadata;
using EmberTrace.Sessions;
using EmberTrace.Tracing;

namespace EmberTrace;

public static partial class Tracer
{
    internal static readonly Profiler Default = new();

    private static readonly ConcurrentDictionary<int, string> IdToName = new();
    private static readonly ConcurrentDictionary<string, int> NameToId = new(StringComparer.Ordinal);
    private static RuntimeMetadataProvider? _runtimeMetadata;
    private static int _runtimeMetadataEnabled;
    private static int _runtimeMetadataRegistered;
    private const string DefaultCategory = "Default";

    static Tracer()
    {
#if DEBUG
        EnableRuntimeMetadata();
#endif
    }

    private static int _idCollisionMode = (int)TracerIdCollisionMode.Warn;

    public static bool IsRunning => Default.IsRunning;

    public static void Start(SessionOptions? options = null) => Default.Start(options);

    public static TraceSession Stop() => Default.Stop();

    public static Scope Scope(int id) => Default.Scope(id);

    public static AsyncScope ScopeAsync(int id) => new AsyncScope(id, Default);

    public static long NewFlowId() => Default.NewFlowId();

    public static long FlowStartNew(int id) => Default.FlowStartNew(id);

    public static FlowScope Flow(int id) => Default.Flow(id);

    public static void FlowStart(int id, long flowId) => Default.FlowStart(id, flowId);

    public static void FlowStep(int id, long flowId) => Default.FlowStep(id, flowId);

    public static void FlowEnd(int id, long flowId) => Default.FlowEnd(id, flowId);

    [RequiresUnreferencedCode("Uses Activity reflection through EmberTrace.ActivityBridge.")]
    public static long FlowFromActivityCurrent(int id)
    {
        if (!IsRunning)
            return 0;

        if (!EmberTrace.ActivityBridge.ActivityBridge.TryGetCurrentFlowId(out var flowId))
            return 0;

        if (flowId == 0)
            return 0;

        Default.FlowStart(id, flowId);
        Default.FlowStep(id, flowId);
        Default.FlowEnd(id, flowId);
        return flowId;
    }

    public static void Instant(int id) => Default.Instant(id);

    public static void Counter(int id, long value) => Default.Counter(id, value);

    public static FlowHandle FlowStartNewHandle(int id) => Default.FlowStartNewHandle(id);

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
        RegisterRuntimeMetadata(id, name);
        return id;
    }

    public static int CategoryId(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return 0;

        return StableId(category);
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

    internal static void EnableRuntimeMetadata()
    {
        Volatile.Write(ref _runtimeMetadataEnabled, 1);
        var provider = EnsureRuntimeMetadataProvider();
        if (provider is null)
            return;

        foreach (var entry in IdToName)
            provider.Register(entry.Key, entry.Value, DefaultCategory);
    }

    private static void RegisterRuntimeMetadata(int id, string name)
    {
        var provider = EnsureRuntimeMetadataProvider();
        if (provider is null)
            return;

        provider.Register(id, name, DefaultCategory);
    }

    private static RuntimeMetadataProvider? EnsureRuntimeMetadataProvider()
    {
        if (Volatile.Read(ref _runtimeMetadataEnabled) == 0)
            return null;

        var provider = _runtimeMetadata;
        if (provider is null)
        {
            var created = new RuntimeMetadataProvider();
            provider = Interlocked.CompareExchange(ref _runtimeMetadata, created, null) ?? created;
        }

        if (Interlocked.CompareExchange(ref _runtimeMetadataRegistered, 1, 0) == 0)
            TraceMetadata.Register(provider);

        return provider;
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
    private readonly Profiler _profiler;

    internal AsyncScope(int id, Profiler profiler)
    {
        _id = id;
        _profiler = profiler;
        _active = profiler.IsRunning;
        if (_active)
            profiler.Scope(id);
    }

    public ValueTask DisposeAsync()
    {
        if (_active)
            _profiler.EndScope(_id);

        return ValueTask.CompletedTask;
    }
}
