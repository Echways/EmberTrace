using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Extensions.Hosting.Recording;
using EmberTrace.Metadata;

namespace EmberTrace.Extensions.Hosting.Tests.Recording;

[TestClass]
[DoNotParallelize]
public sealed class HttpTraceIdsTests
{
    [TestInitialize]
    public void Setup()
    {
        HttpTraceIds.Clear();
        HttpTraceIds.EnsureRegistered();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Tracer.IsRunning)
            Tracer.Stop();

        HttpTraceIds.Clear();
    }

    [TestMethod]
    public void Resolve_IsStableForTheSameName()
    {
        var first = HttpTraceIds.Resolve("GET /orders/{id}", "HTTP GET", "Http", 1024);
        var second = HttpTraceIds.Resolve("GET /orders/{id}", "HTTP GET", "Http", 1024);

        Assert.AreEqual(first, second);
        Assert.AreEqual(Tracer.Id("GET /orders/{id}"), first);
    }

    [TestMethod]
    public void Resolve_FallsBackWhenTheCapIsReached()
    {
        HttpTraceIds.Resolve("GET /a", "HTTP GET", "Http", 1);
        var overflow = HttpTraceIds.Resolve("GET /b", "HTTP GET", "Http", 1);

        Assert.AreEqual(Tracer.Id("HTTP GET"), overflow);
    }

    [TestMethod]
    public void Resolve_ReusesTheFallbackId()
    {
        HttpTraceIds.Resolve("GET /a", "HTTP GET", "Http", 1);
        var first = HttpTraceIds.Resolve("GET /b", "HTTP GET", "Http", 1);
        var second = HttpTraceIds.Resolve("GET /c", "HTTP GET", "Http", 1);

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void RoutesResolvedAfterStart_KeepTheirNameAndCategory()
    {
        Tracer.Start(SessionOptionsFactory.Create(new EmberTraceOptions()));

        var id = HttpTraceIds.Resolve("GET /late/{id}", "HTTP GET", "Http", 1024);
        Tracer.Instant(id);

        var session = Tracer.Stop();

        Assert.IsTrue(session.Metadata.TryGet(id, out var meta));
        Assert.AreEqual("GET /late/{id}", meta.Name);
        Assert.AreEqual("Http", meta.Category);
    }

    [TestMethod]
    public void Provider_IsNotEnumerable()
    {
        var provider = HttpTraceIds.Provider;

        Assert.IsFalse(typeof(IEnumerable<TraceMeta>).IsAssignableFrom(provider.GetType()));
    }

    [TestMethod]
    public void EnsureRegistered_IsIdempotent()
    {
        HttpTraceIds.EnsureRegistered();
        HttpTraceIds.EnsureRegistered();

        var id = HttpTraceIds.Resolve("GET /idempotent", "HTTP GET", "Http", 1024);

        Assert.IsTrue(TraceMetadata.CreateDefault().TryGet(id, out _));
    }
}
