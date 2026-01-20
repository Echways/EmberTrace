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

    public static long NewFlowId() => Profiler.NewFlowId();

    public static long FlowStartNew(int id) => Profiler.FlowStartNew(id);

    public static void FlowStart(int id, long flowId) => Profiler.FlowStart(id, flowId);

    public static void FlowStep(int id, long flowId) => Profiler.FlowStep(id, flowId);

    public static void FlowEnd(int id, long flowId) => Profiler.FlowEnd(id, flowId);

    public static FlowHandle FlowStartNewHandle(int id) => Profiler.FlowStartNewHandle(id);

    public static void FlowEnd(FlowHandle handle) => handle.End();

    public static void FlowStep(FlowHandle handle) => handle.Step();

    public static ITraceMetadataProvider CreateMetadata() => TraceMetadata.CreateDefault();
}