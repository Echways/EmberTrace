namespace EmberTrace.Metadata;

public static class TraceMetadataResolution
{
    public static void Resolve(
        this ITraceMetadataProvider? meta,
        int id,
        out string name,
        out string category)
    {
        if (meta is not null && meta.TryGet(id, out var m))
        {
            name = m.Name;
            category = m.Category ?? string.Empty;
            return;
        }

        name = id.ToString();
        category = string.Empty;
    }
}
