namespace EmberTrace.Tracing;

internal static class AsyncScopeContext
{
    private static readonly AsyncLocal<long> Flowed = new(OnFlowedChanged);

    private static long _nextId;

    [field: ThreadStatic] public static long Current { get; private set; }

    public static long NewId()
    {
        var id = Interlocked.Increment(ref _nextId);
        return id == 0 ? Interlocked.Increment(ref _nextId) : id;
    }

    public static void Set(long scopeId)
    {
        Flowed.Value = scopeId;
        Current = scopeId;
    }

    private static void OnFlowedChanged(AsyncLocalValueChangedArgs<long> args)
    {
        if (args.ThreadContextChanged)
            Current = args.CurrentValue;
    }
}