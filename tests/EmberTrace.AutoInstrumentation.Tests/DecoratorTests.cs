using EmberTrace.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace EmberTrace.AutoInstrumentation.Tests;

[TestClass]
[DoNotParallelize]
public class DecoratorTests
{
    [TestCleanup]
    public void Cleanup()
    {
        if (Tracer.IsRunning)
            Tracer.Stop();
    }

    [TestMethod]
    public void Decorator_RecordsAScopeAndForwardsTheResult()
    {
        IInventoryService service = new TracedInventoryService(new InventoryService());

        Tracer.Start(new SessionOptions());
        var reserved = service.Reserve(7);
        var session = Tracer.Stop();

        Assert.AreEqual(7, reserved);
        Assert.IsTrue(HasScope(session, Tracer.Id("InventoryService.Reserve")));
    }

    [TestMethod]
    public void ForwardedProperty_IsNotTraced()
    {
        IInventoryService service = new TracedInventoryService(new InventoryService());

        Tracer.Start(new SessionOptions());
        var available = service.Available;
        var session = Tracer.Stop();

        Assert.AreEqual(100, available);
        Assert.IsFalse(HasScope(session, Tracer.Id("InventoryService.Available")));
    }

    [TestMethod]
    public async Task RegisteredDecorator_ResolvesFromTheContainer()
    {
        var services = new ServiceCollection();
        services.AddTracedInventoryService();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<IInventoryService>();

        Assert.IsInstanceOfType<TracedInventoryService>(service);
        Assert.AreEqual(3, await service.ReserveAsync(3));
    }

    private static bool HasScope(TraceSession session, int id)
    {
        foreach (var e in session.EnumerateEvents())
            if (e.Id == id && e.Kind == TraceEventKind.Begin)
                return true;

        return false;
    }
}
