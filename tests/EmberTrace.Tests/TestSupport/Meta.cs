using EmberTrace.Metadata;

namespace EmberTrace.Tests;

internal static class Meta
{
    public static ITraceMetadataProvider Of(params (int Id, string Name, string? Category)[] entries)
    {
        return TraceMetadata.FromEntries(entries.Select(e => new TraceMeta(e.Id, e.Name, e.Category)));
    }
}
