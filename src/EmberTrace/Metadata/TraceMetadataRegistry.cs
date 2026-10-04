namespace EmberTrace.Metadata;

public sealed class TraceMetadataRegistry
{
    private readonly List<ITraceMetadataProvider> _registered = new();
    private ITraceMetadataProvider? _snapshot;

    public static TraceMetadataRegistry Shared { get; } = new();

    public void Register(ITraceMetadataProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (_registered)
        {
            _registered.Add(provider);
            Volatile.Write(ref _snapshot, null);
        }
    }

    public bool Unregister(ITraceMetadataProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (_registered)
        {
            if (!_registered.Remove(provider))
                return false;

            Volatile.Write(ref _snapshot, null);
            return true;
        }
    }

    public void Clear()
    {
        lock (_registered)
        {
            _registered.Clear();
            Volatile.Write(ref _snapshot, null);
        }
    }

    public ITraceMetadataProvider CreateProvider()
    {
        var snapshot = Volatile.Read(ref _snapshot);
        if (snapshot is not null)
            return snapshot;

        lock (_registered)
        {
            snapshot = _snapshot;
            if (snapshot is null)
            {
                snapshot = CompositeMetadataProvider.Create(_registered);
                Volatile.Write(ref _snapshot, snapshot);
            }

            return snapshot;
        }
    }
}