using EmberTrace.Abstractions.Attributes;

namespace EmberTrace.AutoInstrumentation.Tests;

public sealed class DisposalLog
{
    public int Sync { get; set; }
    public int Async { get; set; }
}

public interface IConnectionPool
{
    int Lease();
}

[Trace]
public partial class ConnectionPool(DisposalLog log) : IConnectionPool, IDisposable, IAsyncDisposable
{
    public int Lease()
    {
        return 1;
    }

    public void Dispose()
    {
        log.Sync++;
    }

    public ValueTask DisposeAsync()
    {
        log.Async++;
        return ValueTask.CompletedTask;
    }
}
