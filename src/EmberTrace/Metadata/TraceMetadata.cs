namespace EmberTrace.Metadata;

public readonly record struct TraceMeta(int Id, string Name, string? Category);

public static class TraceMetadata
{
    private static readonly List<ITraceMetadataProvider> Registered = new();
    private static ITraceMetadataProvider? _snapshot;

    public static void Register(ITraceMetadataProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (Registered)
        {
            Registered.Add(provider);
            Volatile.Write(ref _snapshot, null);
        }
    }

    public static bool Unregister(ITraceMetadataProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (Registered)
        {
            if (!Registered.Remove(provider))
                return false;

            Volatile.Write(ref _snapshot, null);
            return true;
        }
    }

    public static void Reset()
    {
        lock (Registered)
        {
            Registered.Clear();
            Volatile.Write(ref _snapshot, null);
        }
    }

    public static ITraceMetadataProvider CreateDefault()
    {
        var snapshot = Volatile.Read(ref _snapshot);
        if (snapshot is not null)
            return snapshot;

        lock (Registered)
        {
            snapshot = _snapshot;
            if (snapshot is null)
            {
                snapshot = CompositeMetadataProvider.Create(Registered);
                Volatile.Write(ref _snapshot, snapshot);
            }

            return snapshot;
        }
    }

    public static ITraceMetadataProvider FromEntries(IEnumerable<TraceMeta> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var provider = new DictionaryTraceMetadataProvider();
        foreach (var entry in entries)
            provider.Add(entry.Id, entry.Name, entry.Category);

        return provider;
    }

    internal static ITraceMetadataProvider Combine(ITraceMetadataProvider? current, ITraceMetadataProvider next)
    {
        if (current is null)
            return next;

        if (current is CompositeMetadataProvider composite)
            return composite.Append(next);

        return CompositeMetadataProvider.Create(new[] { current, next });
    }
}