using System.Collections.Frozen;

namespace EmberTrace.Metadata;

internal sealed class CompositeMetadataProvider : ITraceMetadataProvider
{
    private readonly ITraceMetadataProvider[] _fallbacks;
    private readonly FrozenDictionary<int, TraceMeta> _flattened;

    private CompositeMetadataProvider(FrozenDictionary<int, TraceMeta> flattened, ITraceMetadataProvider[] fallbacks)
    {
        _flattened = flattened;
        _fallbacks = fallbacks;
    }

    public bool TryGet(int id, out TraceMeta metadata)
    {
        if (_flattened.TryGetValue(id, out metadata))
            return true;

        for (var i = 0; i < _fallbacks.Length; i++)
            if (_fallbacks[i].TryGet(id, out metadata))
                return true;

        metadata = default;
        return false;
    }

    public static ITraceMetadataProvider Create(IReadOnlyList<ITraceMetadataProvider> providers)
    {
        var flattened = new Dictionary<int, TraceMeta>();
        List<ITraceMetadataProvider>? fallbacks = null;

        for (var i = 0; i < providers.Count; i++)
        {
            var provider = providers[i];
            if (provider is IEnumerable<TraceMeta> entries)
            {
                foreach (var entry in entries)
                    flattened.TryAdd(entry.Id, entry);

                continue;
            }

            (fallbacks ??= new List<ITraceMetadataProvider>()).Add(provider);
        }

        if (flattened.Count == 0 && fallbacks is { Count: 1 })
            return fallbacks[0];

        return new CompositeMetadataProvider(
            flattened.ToFrozenDictionary(),
            fallbacks?.ToArray() ?? Array.Empty<ITraceMetadataProvider>());
    }

    public ITraceMetadataProvider Append(ITraceMetadataProvider provider)
    {
        var merged = new ITraceMetadataProvider[_fallbacks.Length + 1];
        Array.Copy(_fallbacks, merged, _fallbacks.Length);
        merged[^1] = provider;
        return new CompositeMetadataProvider(_flattened, merged);
    }
}