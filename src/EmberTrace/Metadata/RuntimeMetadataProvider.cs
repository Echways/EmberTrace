using System.Collections.Concurrent;
using System.Threading;

namespace EmberTrace.Metadata;

internal sealed class RuntimeMetadataProvider : ITraceMetadataProvider
{
    private readonly ConcurrentDictionary<int, TraceMeta> _entries = new();
    private int _count;

    public bool TryGet(int id, out TraceMeta metadata) => _entries.TryGetValue(id, out metadata);

    public void Register(int id, string name, string category)
    {
        if (!Tracer.WithinNameLimit(Volatile.Read(ref _count)))
            return;

        if (_entries.TryAdd(id, new TraceMeta(id, name, category)))
            Interlocked.Increment(ref _count);
    }
}
