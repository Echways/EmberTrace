using EmberTrace.Tracing;

namespace EmberTrace.Flow;

public sealed class FlowScope : IDisposable
{
    private readonly Profiler _profiler;
    private int _ended;

    internal FlowScope(int id, long flowId, bool active, Profiler profiler)
    {
        Id = id;
        FlowId = flowId;
        _profiler = profiler;
        _ended = active ? 0 : 1;
    }

    public int Id { get; }

    public long FlowId { get; }

    public bool IsValid => FlowId != 0;

    public void Dispose()
    {
        if (!IsValid) return;
        if (Interlocked.Exchange(ref _ended, 1) != 0) return;
        _profiler.FlowEnd(Id, FlowId);
    }

    public void Step()
    {
        if (!IsValid) return;
        if (Volatile.Read(ref _ended) != 0) return;
        _profiler.FlowStep(Id, FlowId);
    }

    public FlowHandle ToHandle()
    {
        if (!IsValid) return new FlowHandle(Id, 0, _profiler);
        if (Interlocked.Exchange(ref _ended, 1) != 0)
            return new FlowHandle(Id, 0, _profiler);
        return new FlowHandle(Id, FlowId, _profiler);
    }
}