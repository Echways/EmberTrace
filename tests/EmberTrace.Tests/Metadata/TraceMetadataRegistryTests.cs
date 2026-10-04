using EmberTrace.Metadata;

namespace EmberTrace.Tests.Metadata;

[TestClass]
public class TraceMetadataRegistryTests
{
    private const int Id = 920_001;

    [TestMethod]
    public void PrivateRegistry_IsInvisibleToTheSharedOne()
    {
        var registry = new TraceMetadataRegistry();
        registry.Register(Entry("Private"));

        Assert.IsTrue(registry.CreateProvider().TryGet(Id, out var meta));
        Assert.AreEqual("Private", meta.Name);
        Assert.IsFalse(TraceMetadataRegistry.Shared.CreateProvider().TryGet(Id, out _));
        Assert.IsFalse(TraceMetadata.CreateDefault().TryGet(Id, out _));
    }

    [TestMethod]
    public void TracingSession_WithItsOwnRegistry_ResolvesNamesFromIt()
    {
        var registry = new TraceMetadataRegistry();
        registry.Register(Entry("Private"));

        using var tracing = new TracingSession(registry);
        tracing.Start();
        tracing.Instant(Id);
        var session = tracing.Stop();

        Assert.IsTrue(session.Metadata.TryGet(Id, out var meta));
        Assert.AreEqual("Private", meta.Name);
        Assert.IsTrue(tracing.CreateMetadata().TryGet(Id, out _));
    }

    [TestMethod]
    public void CreateProvider_CachesUntilTheRegistrationsChange()
    {
        var registry = new TraceMetadataRegistry();
        var provider = Entry("First");
        registry.Register(provider);

        var snapshot = registry.CreateProvider();
        Assert.AreSame(snapshot, registry.CreateProvider());

        Assert.IsTrue(registry.Unregister(provider));
        Assert.IsFalse(registry.Unregister(provider));
        Assert.AreNotSame(snapshot, registry.CreateProvider());
        Assert.IsFalse(registry.CreateProvider().TryGet(Id, out _));
    }

    [TestMethod]
    public void Clear_DropsEveryRegistration()
    {
        var registry = new TraceMetadataRegistry();
        registry.Register(Entry("First"));

        registry.Clear();

        Assert.IsFalse(registry.CreateProvider().TryGet(Id, out _));
    }

    [TestMethod]
    public void NullArguments_Throw()
    {
        var registry = new TraceMetadataRegistry();

        Assert.ThrowsExactly<ArgumentNullException>(() => registry.Register(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => registry.Unregister(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new TracingSession((TraceMetadataRegistry)null!));
    }

    private static ITraceMetadataProvider Entry(string name)
    {
        return TraceMetadata.FromEntries([new TraceMeta(Id, name, "App")]);
    }
}
