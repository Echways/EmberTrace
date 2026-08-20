using System.Collections;

namespace EmberTrace.Metadata;

internal sealed class DictionaryTraceMetadataProvider : ITraceMetadataProvider, IEnumerable<TraceMeta>
{
    private readonly Dictionary<int, TraceMeta> _map = new();

    public IEnumerator<TraceMeta> GetEnumerator()
    {
        return _map.Values.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public bool TryGet(int id, out TraceMeta metadata)
    {
        return _map.TryGetValue(id, out metadata);
    }

    public void Add(int id, string name, string? category = null)
    {
        _map[id] = new TraceMeta(id, name, category);
    }
}