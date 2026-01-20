namespace EmberTrace.Metadata;

public interface ITraceMetadataProvider
{
    bool TryGet(int id, out TraceMeta metadata);
}