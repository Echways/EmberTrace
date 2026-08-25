namespace EmberTrace.Internal.Runtime;

internal interface IRuntimeMetrics
{
    long TotalMemoryBytes { get; }
    long TotalAllocatedBytes { get; }
    int ThreadPoolThreadCount { get; }
    long ThreadPoolPendingWorkItemCount { get; }
    long ThreadPoolCompletedWorkItemCount { get; }
    long ExceptionCount { get; }

    int GcCollectionCount(int generation);

    bool TryGetLatestGcPause(out long index, out long pauseTicks);
}
