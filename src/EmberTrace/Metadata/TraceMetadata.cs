namespace EmberTrace.Metadata;

public readonly record struct TraceMeta(int Id, string Name, string? Category);

public static class TraceMetadata
{
    private static ITraceMetadataProvider? _registered;

    public static void Register(ITraceMetadataProvider provider)
    {
        Interlocked.Exchange(ref _registered, provider);
    }

    public static ITraceMetadataProvider CreateDefault()
    {
        return Volatile.Read(ref _registered) ?? new DictionaryTraceMetadataProvider();
    }
}