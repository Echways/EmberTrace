namespace EmberTrace.Metadata;

internal sealed class CompositeMetadataProvider : ITraceMetadataProvider
{
    private readonly ITraceMetadataProvider[] _providers;

    public CompositeMetadataProvider(ITraceMetadataProvider[] providers)
    {
        _providers = providers;
    }

    public ITraceMetadataProvider[] Providers => _providers;

    public bool TryGet(int id, out TraceMeta metadata)
    {
        for (int i = 0; i < _providers.Length; i++)
        {
            if (_providers[i].TryGet(id, out metadata))
                return true;
        }

        metadata = default;
        return false;
    }
}
