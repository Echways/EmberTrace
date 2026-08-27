using EmberTrace.Abstractions.Attributes;

namespace EmberTrace.Benchmarks;

public partial class TracedService
{
    [Trace]
    public partial int Work(int value);

    private int WorkCore(int value)
    {
        return value + 1;
    }

    [Trace]
    public partial ValueTask<int> WorkAsync(int value);

    private ValueTask<int> WorkAsyncCore(int value)
    {
        return new ValueTask<int>(value + 1);
    }
}
