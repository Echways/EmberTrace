namespace EmberTrace.Public;

public interface ITraceMetadataProvider
{
    bool TryGet(int id, out TraceMeta metadata);
}