using EmberTrace.Metadata;

namespace EmberTrace.Tests.Metadata;

[TestClass]
public class TraceMetadataResolutionTests
{
    private sealed class StubProvider : ITraceMetadataProvider
    {
        private readonly TraceMeta? _meta;

        public StubProvider(TraceMeta? meta)
        {
            _meta = meta;
        }

        public bool TryGet(int id, out TraceMeta metadata)
        {
            if (_meta is { } m && m.Id == id)
            {
                metadata = m;
                return true;
            }

            metadata = default;
            return false;
        }
    }

    [TestMethod]
    public void Resolve_KnownId_ReturnsNameAndCategory()
    {
        var provider = new StubProvider(new TraceMeta(42, "Work", "Jobs"));

        provider.Resolve(42, out var name, out var category);

        Assert.AreEqual("Work", name);
        Assert.AreEqual("Jobs", category);
    }

    [TestMethod]
    public void Resolve_NullCategory_ReturnsEmptyString()
    {
        var provider = new StubProvider(new TraceMeta(42, "Work", null));

        provider.Resolve(42, out _, out var category);

        Assert.AreEqual(string.Empty, category);
    }

    [TestMethod]
    public void Resolve_UnknownId_FallsBackToIdString()
    {
        var provider = new StubProvider(null);

        provider.Resolve(7, out var name, out var category);

        Assert.AreEqual("7", name);
        Assert.AreEqual(string.Empty, category);
    }

    [TestMethod]
    public void Resolve_NullProvider_FallsBackToIdString()
    {
        ITraceMetadataProvider? provider = null;

        provider.Resolve(7, out var name, out var category);

        Assert.AreEqual("7", name);
        Assert.AreEqual(string.Empty, category);
    }
}
