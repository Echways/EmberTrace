using EmberTrace.Sessions;

namespace EmberTrace.Extensions.Hosting.Configuration;

internal static class SessionOptionsFactory
{
    public static SessionOptions Create(EmberTraceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new SessionOptions
        {
            ChunkCapacity = options.ChunkCapacity,
            MaxTotalEvents = options.MaxTotalEvents,
            MaxTotalChunks = options.MaxTotalChunks,
            MaxRetentionWindow = options.MaxRetentionWindow,
            OverflowPolicy = options.OverflowPolicy,
            EnableRuntimeMetadata = options.EnableRuntimeMetadata,
            RuntimeCounters = options.RuntimeCounters,
            RuntimeCounterInterval = options.RuntimeCounterInterval,
            SampleEveryNGlobal = options.SampleEveryNGlobal,
            MaxEventsPerSecond = options.MaxEventsPerSecond,
            EnabledCategoryIds = ToCategoryIds(options.EnabledCategories),
            DisabledCategoryIds = ToCategoryIds(options.DisabledCategories)
        };
    }

    private static int[]? ToCategoryIds(string[]? names)
    {
        if (names is null || names.Length == 0)
            return null;

        var ids = new List<int>(names.Length);
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            ids.Add(Tracer.CategoryId(name));
        }

        return ids.Count == 0 ? null : ids.ToArray();
    }
}
