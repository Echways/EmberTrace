using System.Collections.Concurrent;
using EmberTrace.Metadata;

namespace EmberTrace.Extensions.Hosting.Recording;

internal static class HttpTraceIds
{
    private static readonly ConcurrentDictionary<string, int> Ids = new(StringComparer.Ordinal);
    private static int _registered;

    internal static RouteMetadataProvider Provider { get; } = new();

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
            TraceMetadata.Register(Provider);
    }

    public static int Resolve(string name, string fallbackName, string category, int maxTrackedRoutes)
    {
        if (Ids.TryGetValue(name, out var id))
            return id;

        if (Ids.Count >= maxTrackedRoutes)
            return Ids.TryGetValue(fallbackName, out var fallbackId)
                ? fallbackId
                : Register(fallbackName, category);

        return Register(name, category);
    }

    public static void Clear()
    {
        Ids.Clear();
        Provider.Clear();
    }

    private static int Register(string name, string category)
    {
        var id = Tracer.Id(name);
        Provider.Add(id, name, category);
        Ids[name] = id;
        return id;
    }
}
