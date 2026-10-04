namespace EmberTrace.Metadata;

public readonly record struct TraceMeta(int Id, string Name, string? Category);

public static class TraceMetadata
{
    public static void Register(ITraceMetadataProvider provider)
    {
        TraceMetadataRegistry.Shared.Register(provider);
    }

    public static bool Unregister(ITraceMetadataProvider provider)
    {
        return TraceMetadataRegistry.Shared.Unregister(provider);
    }

    public static void Reset()
    {
        TraceMetadataRegistry.Shared.Clear();
    }

    public static ITraceMetadataProvider CreateDefault()
    {
        return TraceMetadataRegistry.Shared.CreateProvider();
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