using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.AutoInstrumentation.Tests;

[TestClass]
[DoNotParallelize]
public class TracedMethodTests
{
    [TestCleanup]
    public void Cleanup()
    {
        if (Tracer.IsRunning)
            Tracer.Stop();
    }

    [TestMethod]
    public void SyncMethod_ReturnsTheCoreResultInsideOneScope()
    {
        Tracer.Start(new SessionOptions());
        var result = new OrderService().Sum(2, 3);
        var session = Tracer.Stop();

        var id = Tracer.Id("OrderService.Sum");
        var kinds = new List<TraceEventKind>();
        foreach (var e in session.EnumerateEventsSorted())
            if (e.Id == id)
                kinds.Add(e.Kind);

        Assert.AreEqual(5, result);
        CollectionAssert.AreEqual(new[] { TraceEventKind.Begin, TraceEventKind.End }, kinds);
    }

    [TestMethod]
    [DataRow("OrderService.Sum", "Orders")]
    [DataRow("OrderService.GetAsync", "Orders")]
    [DataRow("checkout", "Orders")]
    [DataRow("InventoryService.Reserve", "Inventory")]
    public void GeneratedMetadata_CarriesNameAndCategory(string name, string category)
    {
        Assert.IsTrue(TraceMetadata.CreateDefault().TryGet(Tracer.Id(name), out var meta));
        Assert.AreEqual(name, meta.Name);
        Assert.AreEqual(category, meta.Category);
    }

    [TestMethod]
    public async Task AsyncMethod_NestsTheScopesItCallsAfterAnAwait()
    {
        Tracer.Start(new SessionOptions());
        var result = await new OrderService().GetAsync(21);
        await new OrderService().CheckoutAsync();
        var session = Tracer.Stop();

        var stats = session.Analyze();
        var root = session.Process(groupByThread: false).GlobalRoot;
        var outer = root.Children.Single(c => c.Id == Tracer.Id("OrderService.GetAsync"));

        Assert.AreEqual(42, result);
        Assert.AreEqual(Tracer.Id("OrderService.Inner"), outer.Children.Single().Id);
        Assert.AreEqual(1L, root.Children.Single(c => c.Id == Tracer.Id("checkout")).Count);
        Assert.AreEqual(0L, stats.UnmatchedBeginCount + stats.UnmatchedEndCount);
    }

    [TestMethod]
    public async Task AsyncMethod_WithoutASession_StillReturnsTheCoreResult()
    {
        Assert.AreEqual(10, await new OrderService().GetAsync(5));
    }
}
