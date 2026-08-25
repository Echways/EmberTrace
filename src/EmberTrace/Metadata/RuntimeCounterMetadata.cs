using System.Collections;
using EmberTrace.Sessions;

namespace EmberTrace.Metadata;

internal sealed class RuntimeCounterMetadata : ITraceMetadataProvider, IEnumerable<TraceMeta>
{
    public static readonly RuntimeCounterMetadata Instance = new();

    private static readonly Dictionary<int, TraceMeta> Map = new()
    {
        [RuntimeCounterIds.GcGen0] = new TraceMeta(RuntimeCounterIds.GcGen0, "GC Gen0", RuntimeCounterIds.Category),
        [RuntimeCounterIds.GcGen1] = new TraceMeta(RuntimeCounterIds.GcGen1, "GC Gen1", RuntimeCounterIds.Category),
        [RuntimeCounterIds.GcGen2] = new TraceMeta(RuntimeCounterIds.GcGen2, "GC Gen2", RuntimeCounterIds.Category),
        [RuntimeCounterIds.HeapBytes] =
            new TraceMeta(RuntimeCounterIds.HeapBytes, "Heap bytes", RuntimeCounterIds.Category),
        [RuntimeCounterIds.AllocatedBytes] =
            new TraceMeta(RuntimeCounterIds.AllocatedBytes, "Allocated bytes", RuntimeCounterIds.Category),
        [RuntimeCounterIds.ThreadPoolThreads] =
            new TraceMeta(RuntimeCounterIds.ThreadPoolThreads, "ThreadPool threads", RuntimeCounterIds.Category),
        [RuntimeCounterIds.ThreadPoolQueue] =
            new TraceMeta(RuntimeCounterIds.ThreadPoolQueue, "ThreadPool queue", RuntimeCounterIds.Category),
        [RuntimeCounterIds.ThreadPoolCompleted] =
            new TraceMeta(RuntimeCounterIds.ThreadPoolCompleted, "ThreadPool completed", RuntimeCounterIds.Category),
        [RuntimeCounterIds.Exceptions] =
            new TraceMeta(RuntimeCounterIds.Exceptions, "Exceptions", RuntimeCounterIds.Category),
        [RuntimeCounterIds.GcPause] =
            new TraceMeta(RuntimeCounterIds.GcPause, "GC pause", RuntimeCounterIds.Category)
    };

    private RuntimeCounterMetadata()
    {
    }

    public IEnumerator<TraceMeta> GetEnumerator()
    {
        return Map.Values.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public bool TryGet(int id, out TraceMeta metadata)
    {
        return Map.TryGetValue(id, out metadata);
    }
}
