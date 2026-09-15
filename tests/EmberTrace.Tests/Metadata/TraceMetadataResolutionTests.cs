using EmberTrace.Metadata;

namespace EmberTrace.Tests.Metadata;

[TestClass]
public class TraceMetadataResolutionTests
{
    [TestMethod]
    [DataRow(42, "Work", "Jobs")]
    [DataRow(43, "Idle", "")]
    [DataRow(7, "7", "")]
    [DataRow(-3, "-3", "")]
    public void Resolve_ReturnsNameAndCategoryOrFallsBackToTheId(int id, string expectedName, string expectedCategory)
    {
        var provider = Meta.Of((42, "Work", "Jobs"), (43, "Idle", null));

        provider.Resolve(id, out var name, out var category);

        Assert.AreEqual(expectedName, name);
        Assert.AreEqual(expectedCategory, category);
    }

    [TestMethod]
    public void Resolve_WithoutAProvider_FallsBackToTheId()
    {
        ITraceMetadataProvider? provider = null;

        provider.Resolve(7, out var name, out var category);

        Assert.AreEqual("7", name);
        Assert.AreEqual(string.Empty, category);
    }
}
