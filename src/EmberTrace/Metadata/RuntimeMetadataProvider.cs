using System.Collections.Concurrent;

namespace EmberTrace.Metadata;

internal sealed class RuntimeMetadataProvider : ITraceMetadataProvider
{
    private readonly ConcurrentDictionary<int, TraceMeta> _entries = new();

    public bool TryGet(int id, out TraceMeta metadata) => _entries.TryGetValue(id, out metadata);

    public void Register(int id, string name, string category)
    {
        _entries.TryAdd(id, new TraceMeta(id, name, category));
    }
}
