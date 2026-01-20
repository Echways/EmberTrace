using System.Collections.Generic;

namespace EmberTrace.Metadata;

internal sealed class DictionaryTraceMetadataProvider : ITraceMetadataProvider
{
    private readonly Dictionary<int, TraceMeta> _map = new();

    public void Add(int id, string name, string? category = null)
    {
        _map[id] = new TraceMeta(id, name, category);
    }

    public bool TryGet(int id, out TraceMeta metadata) => _map.TryGetValue(id, out metadata);
}