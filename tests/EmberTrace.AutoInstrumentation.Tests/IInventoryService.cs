using EmberTrace.Abstractions.Attributes;

namespace EmberTrace.AutoInstrumentation.Tests;

public interface IInventoryService
{
    int Available { get; }

    int Reserve(int quantity);

    Task<int> ReserveAsync(int quantity);
}

[Trace]
[TraceCategory("Inventory")]
public partial class InventoryService : IInventoryService
{
    public int Available => 100;

    public int Reserve(int quantity)
    {
        return quantity;
    }

    public Task<int> ReserveAsync(int quantity)
    {
        return Task.FromResult(quantity);
    }
}
