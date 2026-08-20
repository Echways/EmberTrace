using System;
using System.Diagnostics.CodeAnalysis;
using EmberTrace.Flow;
using EmberTrace.Metadata;
using EmberTrace.Sessions;
using EmberTrace.Tracing;

namespace EmberTrace;

public sealed class TracingSession : IDisposable
{
    private readonly Profiler _profiler = new();
    private readonly Action<TraceSession>? _onStopped;
    private bool _disposed;

    public TracingSession()
    {
    }

    public TracingSession(Action<TraceSession> onStopped)
    {
        _onStopped = onStopped ?? throw new ArgumentNullException(nameof(onStopped));
    }

    public bool IsRunning => _profiler.IsRunning;

    public TraceSession? LastSession { get; private set; }

    public void Start(SessionOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _profiler.Start(options);
    }

    public TraceSession Stop()
    {
        var session = _profiler.Stop();
        LastSession = session;
        _onStopped?.Invoke(session);
        return session;
    }

    public Scope Scope(int id) => _profiler.Scope(id);

    public AsyncScope ScopeAsync(int id) => new AsyncScope(id, _profiler);

    public long NewFlowId() => _profiler.NewFlowId();

    public long FlowStartNew(int id) => _profiler.FlowStartNew(id);

    public FlowScope Flow(int id) => _profiler.Flow(id);

    public void FlowStart(int id, long flowId) => _profiler.FlowStart(id, flowId);

    public void FlowStep(int id, long flowId) => _profiler.FlowStep(id, flowId);

    public void FlowEnd(int id, long flowId) => _profiler.FlowEnd(id, flowId);

    [RequiresUnreferencedCode("Uses Activity reflection through EmberTrace.ActivityBridge.")]
    public long FlowFromActivityCurrent(int id)
    {
        if (!IsRunning)
            return 0;

        if (!EmberTrace.ActivityBridge.ActivityBridge.TryGetCurrentFlowId(out var flowId))
            return 0;

        if (flowId == 0)
            return 0;

        _profiler.FlowStart(id, flowId);
        _profiler.FlowStep(id, flowId);
        _profiler.FlowEnd(id, flowId);
        return flowId;
    }

    public void Instant(int id) => _profiler.Instant(id);

    public void Counter(int id, long value) => _profiler.Counter(id, value);

    public FlowHandle FlowStartNewHandle(int id) => _profiler.FlowStartNewHandle(id);

    public void FlowEnd(FlowHandle handle) => handle.End();

    public void FlowStep(FlowHandle handle) => handle.Step();

    public ITraceMetadataProvider CreateMetadata() => _profiler.Metadata;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_profiler.IsRunning)
            Stop();
    }
}
