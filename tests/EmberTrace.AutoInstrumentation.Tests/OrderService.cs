using EmberTrace.Abstractions.Attributes;

namespace EmberTrace.AutoInstrumentation.Tests;

[TraceCategory("Orders")]
public partial class OrderService
{
    [Trace]
    public partial int Sum(int a, int b);

    private int SumCore(int a, int b)
    {
        return a + b;
    }

    [Trace]
    public partial Task<int> GetAsync(int id);

    private async Task<int> GetAsyncCore(int id)
    {
        await Task.Yield();
        return Inner(id);
    }

    [Trace]
    public partial int Inner(int id);

    private int InnerCore(int id)
    {
        return id * 2;
    }

    [Trace("checkout")]
    public partial ValueTask CheckoutAsync();

    private ValueTask CheckoutAsyncCore()
    {
        return default;
    }
}
