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
    public async Task Decorator_TracesMethodsForwardsResultsAndLeavesPropertiesUntraced()
    {
        IInventoryService service = new TracedInventoryService(new InventoryService());

        Tracer.Start(new SessionOptions());
        var reserved = service.Reserve(7);
        var reservedAsync = await service.ReserveAsync(3);
        var available = service.Available;
        var session = Tracer.Stop();

        Assert.AreEqual(7, reserved);
        Assert.AreEqual(3, reservedAsync);
        Assert.AreEqual(100, available);
        CollectionAssert.AreEqual(
            new[] { Tracer.Id("InventoryService.Reserve"), Tracer.Id("InventoryService.ReserveAsync") },
            Scopes(session));
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

    [TestMethod]
    public void RegisteredDecorator_DisposesTheServiceWithTheScope()
    {
        var log = new DisposalLog();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddTracedConnectionPool();

        using (var provider = services.BuildServiceProvider())
        using (var scope = provider.CreateScope())
            Assert.AreEqual(1, scope.ServiceProvider.GetRequiredService<IConnectionPool>().Lease());

        Assert.AreEqual(1, log.Sync);
        Assert.AreEqual(0, log.Async);
    }

    [TestMethod]
    public async Task RegisteredDecorator_DisposesTheServiceAsynchronously()
    {
        var log = new DisposalLog();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddTracedConnectionPool();

        await using (var provider = services.BuildServiceProvider())
        await using (var scope = provider.CreateAsyncScope())
            Assert.AreEqual(1, scope.ServiceProvider.GetRequiredService<IConnectionPool>().Lease());

        Assert.AreEqual(0, log.Sync);
        Assert.AreEqual(1, log.Async);
    }

    private static List<int> Scopes(TraceSession session)
    {
        var ids = new List<int>();
        foreach (var e in session.EnumerateEventsSorted())
            if (e.Kind == TraceEventKind.Begin)
                ids.Add(e.Id);

        return ids;
    }
}
