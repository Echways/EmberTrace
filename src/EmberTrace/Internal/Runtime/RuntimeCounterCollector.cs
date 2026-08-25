using EmberTrace.Sessions;

namespace EmberTrace.Internal.Runtime;

internal interface IRuntimeCounterSink
{
    void Counter(int id, long value);

    void Span(int id, long startTimestamp, long endTimestamp);
}

internal sealed class RuntimeCounterCollector
{
    private readonly RuntimeCounters _enabled;
    private readonly IRuntimeMetrics _metrics;

    private bool _hasBaseline;
    private long _lastAllocatedBytes;
    private long _lastCompletedWorkItems;
    private long _lastExceptionCount;
    private long _lastGcIndex;
    private int _lastGen0;
    private int _lastGen1;
    private int _lastGen2;

    public RuntimeCounterCollector(RuntimeCounters enabled, IRuntimeMetrics metrics)
    {
        _enabled = enabled;
        _metrics = metrics;
    }

    public void Sample(long timestamp, IRuntimeCounterSink sink)
    {
        if ((_enabled & RuntimeCounters.Gc) != 0)
            SampleGc(sink);

        if ((_enabled & RuntimeCounters.Memory) != 0)
            SampleMemory(sink);

        if ((_enabled & RuntimeCounters.ThreadPool) != 0)
            SampleThreadPool(sink);

        if ((_enabled & RuntimeCounters.Exceptions) != 0)
            SampleExceptions(sink);

        if ((_enabled & RuntimeCounters.GcPauses) != 0)
            SampleGcPause(timestamp, sink);

        _hasBaseline = true;
    }

    private void SampleGc(IRuntimeCounterSink sink)
    {
        var gen0 = _metrics.GcCollectionCount(0);
        var gen1 = _metrics.GcCollectionCount(1);
        var gen2 = _metrics.GcCollectionCount(2);

        sink.Counter(RuntimeCounterIds.GcGen0, Delta(_lastGen0, gen0));
        sink.Counter(RuntimeCounterIds.GcGen1, Delta(_lastGen1, gen1));
        sink.Counter(RuntimeCounterIds.GcGen2, Delta(_lastGen2, gen2));

        _lastGen0 = gen0;
        _lastGen1 = gen1;
        _lastGen2 = gen2;
    }

    private void SampleMemory(IRuntimeCounterSink sink)
    {
        var allocated = _metrics.TotalAllocatedBytes;

        sink.Counter(RuntimeCounterIds.HeapBytes, _metrics.TotalMemoryBytes);
        sink.Counter(RuntimeCounterIds.AllocatedBytes, Delta(_lastAllocatedBytes, allocated));

        _lastAllocatedBytes = allocated;
    }

    private void SampleThreadPool(IRuntimeCounterSink sink)
    {
        var completed = _metrics.ThreadPoolCompletedWorkItemCount;

        sink.Counter(RuntimeCounterIds.ThreadPoolThreads, _metrics.ThreadPoolThreadCount);
        sink.Counter(RuntimeCounterIds.ThreadPoolQueue, _metrics.ThreadPoolPendingWorkItemCount);
        sink.Counter(RuntimeCounterIds.ThreadPoolCompleted, Delta(_lastCompletedWorkItems, completed));

        _lastCompletedWorkItems = completed;
    }

    private void SampleExceptions(IRuntimeCounterSink sink)
    {
        var count = _metrics.ExceptionCount;

        sink.Counter(RuntimeCounterIds.Exceptions, Delta(_lastExceptionCount, count));

        _lastExceptionCount = count;
    }

    private void SampleGcPause(long timestamp, IRuntimeCounterSink sink)
    {
        if (!_metrics.TryGetLatestGcPause(out var index, out var pauseTicks))
            return;

        if (index <= _lastGcIndex)
            return;

        _lastGcIndex = index;

        if (pauseTicks <= 0)
            return;

        var start = timestamp - pauseTicks;
        if (start < 0)
            start = 0;

        sink.Span(RuntimeCounterIds.GcPause, start, timestamp);
    }

    private long Delta(long previous, long current)
    {
        if (!_hasBaseline)
            return 0;

        var delta = current - previous;
        return delta < 0 ? 0 : delta;
    }
}
