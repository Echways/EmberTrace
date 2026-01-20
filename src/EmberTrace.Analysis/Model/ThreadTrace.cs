namespace EmberTrace.Analysis.Model;

public sealed class ThreadTrace
{
    public required int ThreadId { get; init; }
    public required CallTreeNode Root { get; init; }
}
