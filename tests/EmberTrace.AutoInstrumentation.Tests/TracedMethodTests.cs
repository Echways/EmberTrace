using EmberTrace.Analysis;
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
    public void SyncMethod_RecordsABeginAndAnEnd()
    {
        Tracer.Start(new SessionOptions());
        var result = new OrderService().Sum(2, 3);
        var session = Tracer.Stop();

        Assert.AreEqual(5, result);

        var begins = 0;
        var ends = 0;
        var id = Tracer.Id("OrderService.Sum");

        foreach (var e in session.EnumerateEvents())
        {
            if (e.Id != id)
                continue;

            if (e.Kind == TraceEventKind.Begin)
                begins++;
            if (e.Kind == TraceEventKind.End)
                ends++;
        }

        Assert.AreEqual(1, begins);
        Assert.AreEqual(1, ends);
    }

    [TestMethod]
    public void GeneratedMetadata_CarriesNameAndCategory()
    {
        Tracer.Start(new SessionOptions());
        new OrderService().Sum(1, 1);
        var session = Tracer.Stop();

        Assert.IsTrue(session.Metadata.TryGet(Tracer.Id("OrderService.Sum"), out var meta));
        Assert.AreEqual("OrderService.Sum", meta.Name);
        Assert.AreEqual("Orders", meta.Category);
    }

    [TestMethod]
    public async Task ExplicitName_OverridesTheDefault()
    {
        Tracer.Start(new SessionOptions());
        await new OrderService().CheckoutAsync();
        var session = Tracer.Stop();

        Assert.IsTrue(session.Metadata.TryGet(Tracer.Id("checkout"), out var meta));
        Assert.AreEqual("checkout", meta.Name);
    }

    [TestMethod]
    public async Task AsyncMethod_NestsTheInnerScope()
    {
        Tracer.Start(new SessionOptions());
        var result = await new OrderService().GetAsync(21);
        var session = Tracer.Stop();

        Assert.AreEqual(42, result);

        var ids = session.Analyze().ByTotalTimeDesc.Select(stat => stat.Id).ToList();

        CollectionAssert.Contains(ids, Tracer.Id("OrderService.GetAsync"));
        CollectionAssert.Contains(ids, Tracer.Id("OrderService.Inner"));
    }

    [TestMethod]
    public async Task AsyncMethod_RecordsNothingWhenNoSessionRuns()
    {
        var result = await new OrderService().GetAsync(5);

        Assert.AreEqual(10, result);
        Assert.IsFalse(Tracer.IsRunning);
    }
}
