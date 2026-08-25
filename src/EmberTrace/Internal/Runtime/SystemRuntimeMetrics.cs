using System.Runtime.ExceptionServices;
using EmberTrace.Internal.Time;

namespace EmberTrace.Internal.Runtime;

internal sealed class SystemRuntimeMetrics : IRuntimeMetrics, IDisposable
{
    private readonly bool _countExceptions;
    private long _exceptionCount;

    public SystemRuntimeMetrics(bool countExceptions)
    {
        _countExceptions = countExceptions;

        if (countExceptions)
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
    }

    public long TotalMemoryBytes => GC.GetTotalMemory(false);

    public long TotalAllocatedBytes => GC.GetTotalAllocatedBytes(false);

    public int ThreadPoolThreadCount => ThreadPool.ThreadCount;

    public long ThreadPoolPendingWorkItemCount => ThreadPool.PendingWorkItemCount;

    public long ThreadPoolCompletedWorkItemCount => ThreadPool.CompletedWorkItemCount;

    public long ExceptionCount => Interlocked.Read(ref _exceptionCount);

    public int GcCollectionCount(int generation)
    {
        return GC.CollectionCount(generation);
    }

    public bool TryGetLatestGcPause(out long index, out long pauseTicks)
    {
        var info = GC.GetGCMemoryInfo();
        index = info.Index;

        if (index <= 0)
        {
            pauseTicks = 0;
            return false;
        }

        long elapsed = 0;
        foreach (var pause in info.PauseDurations)
            elapsed += pause.Ticks;

        pauseTicks = elapsed <= 0 ? 0 : elapsed * Timestamp.Frequency / TimeSpan.TicksPerSecond;
        return true;
    }

    public void Dispose()
    {
        if (_countExceptions)
            AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;
    }

    private void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
    {
        Interlocked.Increment(ref _exceptionCount);
    }
}
