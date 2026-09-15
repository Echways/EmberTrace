using EmberTrace.Metadata;
using EmberTrace.Sessions;
using EmberTrace.Tracing;

namespace EmberTrace.Tests.Tracing;

[TestClass]
public class CategoryFilterTests
{
    private const int Fetch = 1;
    private const int Draw = 2;
    private const int Play = 3;
    private const int Uncategorized = 4;
    private const int Unknown = 9999;

    [TestMethod]
    [DataRow(null, null, false)]
    [DataRow("", "", false)]
    [DataRow("Network", null, true)]
    [DataRow(null, "Network", true)]
    public void IsActive_OnlyWithANonEmptyList(string? enabled, string? disabled, bool expected)
    {
        Assert.AreEqual(expected, new CategoryFilter(Metadata(), Ids(enabled), Ids(disabled)).IsActive);
    }

    [TestMethod]
    [DataRow(null, null, Fetch, true)]
    [DataRow(null, null, Unknown, true)]
    [DataRow("Network", null, Fetch, true)]
    [DataRow("Network", null, Draw, false)]
    [DataRow("Network", null, Uncategorized, false)]
    [DataRow("Network", null, Unknown, false)]
    [DataRow("Network,Audio", null, Play, true)]
    [DataRow("Network,Audio", null, Draw, false)]
    [DataRow(null, "Rendering", Draw, false)]
    [DataRow(null, "Rendering", Fetch, true)]
    [DataRow(null, "Rendering", Uncategorized, true)]
    [DataRow(null, "Rendering", Unknown, true)]
    [DataRow("Network", "Network", Fetch, true)]
    [DataRow("Network", "Audio", Play, false)]
    public void Allows_ResolvesTheCategoryThroughMetadata(string? enabled, string? disabled, int id, bool expected)
    {
        var filter = new CategoryFilter(Metadata(), Ids(enabled), Ids(disabled));

        Assert.AreEqual(expected, filter.Allows(id));
    }

    [TestMethod]
    public void Allows_ResolvesEachIdOnlyOnce()
    {
        var metadata = new CountingMetadataProvider(Metadata());
        var filter = new CategoryFilter(metadata, Ids("Network"), null);

        for (var i = 0; i < 10; i++)
        {
            Assert.IsTrue(filter.Allows(Fetch));
            Assert.IsFalse(filter.Allows(Unknown));
        }

        Assert.AreEqual(2, metadata.Lookups);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Session_AppliesTheFilterBeforeRecording(bool allowList)
    {
        var categorized = Tracer.Id("CategoryFilterTests.Categorized");
        var category = new[] { Tracer.CategoryId("Default") };

        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions
        {
            EnableRuntimeMetadata = true,
            EnabledCategoryIds = allowList ? category : null,
            DisabledCategoryIds = allowList ? null : category
        });

        tracing.Instant(categorized);
        tracing.Instant(Unknown);

        var recorded = tracing.Stop().Events().Select(e => e.Id).ToArray();

        CollectionAssert.AreEqual(new[] { allowList ? categorized : Unknown }, recorded);
    }

    private static DictionaryTraceMetadataProvider Metadata()
    {
        var meta = new DictionaryTraceMetadataProvider();
        meta.Add(Fetch, "Fetch", "Network");
        meta.Add(Draw, "DrawFrame", "Rendering");
        meta.Add(Play, "PlaySound", "Audio");
        meta.Add(Uncategorized, "ComputeAI");
        return meta;
    }

    private static int[]? Ids(string? categories)
    {
        return categories?.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Tracer.CategoryId).ToArray();
    }

    private sealed class CountingMetadataProvider(ITraceMetadataProvider inner) : ITraceMetadataProvider
    {
        public int Lookups { get; private set; }

        public bool TryGet(int id, out TraceMeta metadata)
        {
            Lookups++;
            return inner.TryGet(id, out metadata);
        }
    }
}
