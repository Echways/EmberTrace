using System;
using System.Threading.Tasks;
using EmberTrace.Flow;
using EmberTrace.Metadata;
using EmberTrace.Sessions;
using EmberTrace.Tracing;

namespace EmberTrace;

public static class Tracer
{
    public static bool IsRunning => Profiler.IsRunning;

    public static void Start(SessionOptions? options = null) => Profiler.Start(options);

    public static TraceSession Stop() => Profiler.Stop();

    public static Scope Scope(int id) => Profiler.Scope(id);

    public static AsyncScope ScopeAsync(int id) => new AsyncScope(id);

    public static long NewFlowId() => Profiler.NewFlowId();

    public static long FlowStartNew(int id) => Profiler.FlowStartNew(id);

    public static void FlowStart(int id, long flowId) => Profiler.FlowStart(id, flowId);

    public static void FlowStep(int id, long flowId) => Profiler.FlowStep(id, flowId);

    public static void FlowEnd(int id, long flowId) => Profiler.FlowEnd(id, flowId);

    public static FlowHandle FlowStartNewHandle(int id) => Profiler.FlowStartNewHandle(id);

    public static void FlowEnd(FlowHandle handle) => handle.End();

    public static void FlowStep(FlowHandle handle) => handle.Step();

    public static ITraceMetadataProvider CreateMetadata() => TraceMetadata.CreateDefault();

    public static int Id(string name) => StableId(name);

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
