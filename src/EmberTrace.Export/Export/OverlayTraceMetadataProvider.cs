using EmberTrace.Metadata;

namespace EmberTrace.Export;

internal sealed class OverlayTraceMetadataProvider : ITraceMetadataProvider
{
    private readonly ITraceMetadataProvider _base;
    private readonly int _id;
    private readonly TraceMeta _meta;

    public OverlayTraceMetadataProvider(ITraceMetadataProvider @base, int id, string name)
    {
        _base = @base;
        _id = id;
        _meta = new TraceMeta(id, name, "Marked");
    }

    public bool TryGet(int id, out TraceMeta metadata)
    {
        if (id == _id)
        {
            metadata = _meta;
            return true;
        }

        return _base.TryGet(id, out metadata);
    }
}
