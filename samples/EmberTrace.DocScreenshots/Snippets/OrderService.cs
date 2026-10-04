using EmberTrace.Abstractions.Attributes;

namespace EmberTrace.DocScreenshots;

[TraceCategory("Orders")]
internal sealed partial class OrderService
{
    [Trace]
    public partial Task<int> PlaceAsync(int quantity);

    private async Task<int> PlaceAsyncCore(int quantity)
    {
        await Task.Delay(2).ConfigureAwait(false);
        return Price(quantity);
    }

    [Trace]
    public partial int Price(int quantity);

    private int PriceCore(int quantity)
    {
        Thread.SpinWait(20_000);
        return quantity * 10;
    }
}
