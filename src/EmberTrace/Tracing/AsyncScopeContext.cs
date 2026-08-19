using System.Threading;

namespace EmberTrace.Tracing;

internal static class AsyncScopeContext
{
    private static readonly AsyncLocal<long> Flowed = new(OnFlowedChanged);

    [ThreadStatic] private static long _current;

    private static long _nextId;

    public static long Current => _current;

    public static long NewId()
    {
        var id = Interlocked.Increment(ref _nextId);
        return id == 0 ? Interlocked.Increment(ref _nextId) : id;
    }

    public static void Set(long scopeId)
    {
        Flowed.Value = scopeId;
        _current = scopeId;
    }

    private static void OnFlowedChanged(AsyncLocalValueChangedArgs<long> args)
    {
        if (args.ThreadContextChanged)
            _current = args.CurrentValue;
    }
}
