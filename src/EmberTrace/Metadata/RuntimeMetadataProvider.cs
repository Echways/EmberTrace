using System;
using System.Collections.Concurrent;
using System.Threading;

namespace EmberTrace.Metadata;

internal sealed class RuntimeMetadataProvider : ITraceMetadataProvider
{
    private readonly ConcurrentDictionary<int, TraceMeta> _entries = new();
    private int _count;
    private int _maxEntries;

    public RuntimeMetadataProvider(int maxEntries = 0)
    {
        _maxEntries = maxEntries < 0 ? 0 : maxEntries;
    }

    public int MaxEntries
    {
        get => Volatile.Read(ref _maxEntries);
        set => Volatile.Write(ref _maxEntries, value < 0 ? 0 : value);
    }

    public bool TryGet(int id, out TraceMeta metadata) => _entries.TryGetValue(id, out metadata);

    public bool TryRegister(int id, string name, string category, out string owner)
    {
        while (true)
        {
            if (_entries.TryGetValue(id, out var current))
            {
                owner = current.Name;
                return string.Equals(current.Name, name, StringComparison.Ordinal);
            }

            var limit = MaxEntries;
            if (limit != 0 && Volatile.Read(ref _count) >= limit)
            {
                owner = name;
                return true;
            }

            if (_entries.TryAdd(id, new TraceMeta(id, name, category)))
            {
                Interlocked.Increment(ref _count);
                owner = name;
                return true;
            }
        }
    }
}
