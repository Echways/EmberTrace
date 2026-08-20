using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using EmberTrace.Flow;
using EmberTrace.Internal;
using EmberTrace.Metadata;
using EmberTrace.Sessions;
using EmberTrace.Tracing;

namespace EmberTrace;

public static class Tracer
{
    private const string DefaultCategory = "Default";
    private const int DefaultMaxTrackedNames = 16_384;
    internal static readonly Profiler Default = new();

    internal static readonly RuntimeMetadataProvider Names = new(DefaultMaxTrackedNames);

    private static int _idCollisionMode =
        (int)RuntimeConfig.GetEnum("EmberTrace.IdCollisionMode", TracerIdCollisionMode.Warn);

    public static bool IsRunning => Default.IsRunning;

    public static TracerIdCollisionMode IdCollisionMode
    {
        get => (TracerIdCollisionMode)Volatile.Read(ref _idCollisionMode);
        set => Volatile.Write(ref _idCollisionMode, (int)value);
    }

    public static Action<TracerIdCollision>? OnIdCollision { get; set; }

    public static int MaxTrackedNames
    {
        get => Names.MaxEntries;
        set => Names.MaxEntries = value;
    }

    public static void Start(SessionOptions? options = null)
    {
        Default.Start(options);
    }

    public static TraceSession Stop()
    {
        return Default.Stop();
    }

    public static Scope Scope(int id)
    {
        return Default.Scope(id);
    }

    public static AsyncScope ScopeAsync(int id)
    {
        return new AsyncScope(id, Default);
    }

    public static long NewFlowId()
    {
        return Default.NewFlowId();
    }

    public static long FlowStartNew(int id)
    {
        return Default.FlowStartNew(id);
    }

    public static FlowScope Flow(int id)
    {
        return Default.Flow(id);
    }

    public static void FlowStart(int id, long flowId)
    {
        Default.FlowStart(id, flowId);
    }

    public static void FlowStep(int id, long flowId)
    {
        Default.FlowStep(id, flowId);
    }

    public static void FlowEnd(int id, long flowId)
    {
        Default.FlowEnd(id, flowId);
    }

    [RequiresUnreferencedCode("Uses Activity reflection through EmberTrace.ActivityBridge.")]
    public static long FlowFromActivityCurrent(int id)
    {
        if (!IsRunning)
            return 0;

        if (!ActivityBridge.ActivityBridge.TryGetCurrentFlowId(out var flowId))
            return 0;

        if (flowId == 0)
            return 0;

        Default.FlowStart(id, flowId);
        Default.FlowStep(id, flowId);
        Default.FlowEnd(id, flowId);
        return flowId;
    }

    public static void Instant(int id)
    {
        Default.Instant(id);
    }

    public static void Counter(int id, long value)
    {
        Default.Counter(id, value);
    }

    public static FlowHandle FlowStartNewHandle(int id)
    {
        return Default.FlowStartNewHandle(id);
    }

    public static void FlowEnd(FlowHandle handle)
    {
        handle.End();
    }

    public static void FlowStep(FlowHandle handle)
    {
        handle.Step();
    }

    public static ITraceMetadataProvider CreateMetadata()
    {
        return Default.Metadata;
    }

    public static int Id(string name)
    {
        var id = TraceIds.Stable(name);

        if (!Names.TryRegister(id, name, DefaultCategory, out var owner))
            HandleIdCollision(id, owner, name);

        return id;
    }

    public static int CategoryId(string category)
    {
        return TraceIds.Category(category);
    }

    private static void HandleIdCollision(int id, string existingName, string newName)
    {
        var mode = IdCollisionMode;
        if (mode == TracerIdCollisionMode.Ignore)
            return;

        var collision = new TracerIdCollision(id, existingName, newName);
        var handler = OnIdCollision;
        handler?.Invoke(collision);

        if (mode == TracerIdCollisionMode.Throw)
            throw new InvalidOperationException(collision.ToString());

        if (mode == TracerIdCollisionMode.Warn && handler is null)
            Trace.TraceWarning(collision.ToString());
    }
}

public readonly struct TracerIdCollision(int id, string existingName, string newName)
{
    public int Id { get; } = id;
    public string ExistingName { get; } = existingName;
    public string NewName { get; } = newName;

    public override string ToString()
    {
        return $"Tracer.Id collision: '{ExistingName}' and '{NewName}' map to {Id}.";
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
    private readonly long _scopeId;
    private readonly long _parentScopeId;
    private readonly Profiler? _profiler;

    internal AsyncScope(int id, Profiler profiler)
    {
        _id = id;

        if (!profiler.IsRunning)
        {
            _profiler = null;
            _scopeId = 0;
            _parentScopeId = 0;
            return;
        }

        _profiler = profiler;
        _parentScopeId = AsyncScopeContext.Current;
        _scopeId = AsyncScopeContext.NewId();

        profiler.BeginAsyncScope(id, _scopeId, _parentScopeId);
        AsyncScopeContext.Set(_scopeId);
    }

    public ValueTask DisposeAsync()
    {
        if (_profiler is not null)
        {
            AsyncScopeContext.Set(_parentScopeId);
            _profiler.EndAsyncScope(_id, _scopeId, _parentScopeId);
        }

        return ValueTask.CompletedTask;
    }
}