using System.Diagnostics.CodeAnalysis;
using EmberTrace.Flow;
using EmberTrace.Metadata;
using EmberTrace.Sessions;
using EmberTrace.Tracing;

namespace EmberTrace;

public sealed class TracingSession : IDisposable
{
    private readonly Profiler _profiler = new();

    public bool IsRunning => _profiler.IsRunning;

    public void Start(SessionOptions? options = null) => _profiler.Start(options);

    public TraceSession Stop() => _profiler.Stop();

    public Scope Scope(int id) => _profiler.Scope(id);

    public AsyncScope ScopeAsync(int id) => new AsyncScope(id, _profiler);

    public long NewFlowId() => _profiler.NewFlowId();

    public long FlowStartNew(int id) => _profiler.FlowStartNew(id);

    public FlowScope Flow(int id) => _profiler.Flow(id);

    public void FlowStart(int id, long flowId) => _profiler.FlowStart(id, flowId);

    public void FlowStep(int id, long flowId) => _profiler.FlowStep(id, flowId);

    public void FlowEnd(int id, long flowId) => _profiler.FlowEnd(id, flowId);

    public void Instant(int id) => _profiler.Instant(id);

    public void Counter(int id, long value) => _profiler.Counter(id, value);

    public FlowHandle FlowStartNewHandle(int id) => _profiler.FlowStartNewHandle(id);

    public void FlowEnd(FlowHandle handle) => handle.End();

    public void FlowStep(FlowHandle handle) => handle.Step();

    public ITraceMetadataProvider CreateMetadata() => TraceMetadata.CreateDefault();

    public void Dispose()
    {
        if (_profiler.IsRunning)
            _profiler.Stop();
    }
}
