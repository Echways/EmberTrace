using System.Threading;

namespace EmberTrace.Internal;

internal static class ThreadIdentity
{
    private static long _next;

    [ThreadStatic] private static long _current;

    public static long Current
    {
        get
        {
            var id = _current;
            return id != 0 ? id : _current = Interlocked.Increment(ref _next);
        }
    }
}
