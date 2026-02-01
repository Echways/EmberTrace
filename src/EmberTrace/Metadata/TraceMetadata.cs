using System;
using System.Threading;

namespace EmberTrace.Metadata;

public readonly record struct TraceMeta(int Id, string Name, string? Category);

public static class TraceMetadata
{
    private static ITraceMetadataProvider? _registered;

    public static void Register(ITraceMetadataProvider provider)
    {
        if (provider is null) throw new ArgumentNullException(nameof(provider));

        while (true)
        {
            var current = Volatile.Read(ref _registered);
            var next = Compose(current, provider);

            if (Interlocked.CompareExchange(ref _registered, next, current) == current)
                return;
        }
    }

    public static ITraceMetadataProvider CreateDefault()
    {
        return Volatile.Read(ref _registered) ?? new DictionaryTraceMetadataProvider();
    }

    private static ITraceMetadataProvider Compose(ITraceMetadataProvider? current, ITraceMetadataProvider next)
    {
        if (current is null)
            return next;

        if (current is CompositeMetadataProvider composite)
        {
            var providers = composite.Providers;
            var merged = new ITraceMetadataProvider[providers.Length + 1];
            Array.Copy(providers, merged, providers.Length);
            merged[providers.Length] = next;
            return new CompositeMetadataProvider(merged);
        }

        return new CompositeMetadataProvider(new[] { current, next });
    }
}
