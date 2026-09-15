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
    public void Resolve_IsStableAndRegistersNameAndCategory()
    {
        var first = HttpTraceIds.Resolve("GET /orders/{id}", "HTTP GET", "Web", 1024);
        var second = HttpTraceIds.Resolve("GET /orders/{id}", "HTTP GET", "Other", 1024);

        Assert.AreEqual(Tracer.Id("GET /orders/{id}"), first);
        Assert.AreEqual(first, second);
        Assert.IsTrue(TraceMetadata.CreateDefault().TryGet(first, out var meta));
        Assert.AreEqual(new TraceMeta(first, "GET /orders/{id}", "Web"), meta);
    }

    [TestMethod]
    public void Resolve_BeyondTheCap_SharesOneFallbackIdButKeepsKnownRoutes()
    {
        var known = HttpTraceIds.Resolve("GET /a", "HTTP GET", "Http", 1);
        var overflow = HttpTraceIds.Resolve("GET /b", "HTTP GET", "Http", 1);
        var another = HttpTraceIds.Resolve("GET /c", "HTTP GET", "Http", 1);

        Assert.AreEqual(Tracer.Id("HTTP GET"), overflow);
        Assert.AreEqual(overflow, another);
        Assert.AreEqual(known, HttpTraceIds.Resolve("GET /a", "HTTP GET", "Http", 1));
    }

    [TestMethod]
    public void RoutesResolvedAfterTheSessionStarted_KeepTheirNameAndCategory()
    {
        Tracer.Start(SessionOptionsFactory.Create(new EmberTraceOptions()));

        var id = HttpTraceIds.Resolve("GET /late/{id}", "HTTP GET", "Http", 1024);
        Tracer.Instant(id);

        Assert.IsTrue(Tracer.Stop().Metadata.TryGet(id, out var meta));
        Assert.AreEqual(new TraceMeta(id, "GET /late/{id}", "Http"), meta);
    }

    [TestMethod]
    public void Clear_ForgetsRoutesAndTheirMetadata()
    {
        var id = HttpTraceIds.Resolve("GET /gone", "HTTP GET", "Http", 1);

        HttpTraceIds.Clear();

        Assert.IsFalse(HttpTraceIds.Provider.TryGet(id, out _));
        Assert.AreEqual(Tracer.Id("GET /fresh"), HttpTraceIds.Resolve("GET /fresh", "HTTP GET", "Http", 1));
    }
}
