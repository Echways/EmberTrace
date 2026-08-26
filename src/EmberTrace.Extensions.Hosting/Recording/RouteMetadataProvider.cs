using System.Collections.Concurrent;
using EmberTrace.Metadata;

namespace EmberTrace.Extensions.Hosting.Recording;

internal sealed class RouteMetadataProvider : ITraceMetadataProvider
{
    private readonly ConcurrentDictionary<int, TraceMeta> _map = new();

    public bool TryGet(int id, out TraceMeta metadata)
    {
        return _map.TryGetValue(id, out metadata);
    }

    public void Add(int id, string name, string category)
    {
        _map[id] = new TraceMeta(id, name, category);
    }

    public void Clear()
    {
        _map.Clear();
    }
}
