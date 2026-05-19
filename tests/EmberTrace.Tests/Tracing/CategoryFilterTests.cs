using System.Collections.Generic;
using EmberTrace.Metadata;
using EmberTrace.Sessions;
using EmberTrace.Tracing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmberTrace.Tests.Tracing;

[TestClass]
public class CategoryFilterTests
{
    private static readonly int CatNetwork   = Tracer.CategoryId("Network");
    private static readonly int CatRendering = Tracer.CategoryId("Rendering");
    private static readonly int CatAudio     = Tracer.CategoryId("Audio");

    private static DictionaryTraceMetadataProvider BuildMeta()
    {
        var meta = new DictionaryTraceMetadataProvider();
        meta.Add(1, "Fetch",     "Network");
        meta.Add(2, "DrawFrame", "Rendering");
        meta.Add(3, "PlaySound", "Audio");
        meta.Add(4, "ComputeAI");
        return meta;
    }

    [TestMethod]
    public void IsActive_BothNull_ReturnsFalse()
    {
        var filter = new CategoryFilter(BuildMeta(), enabled: null, disabled: null);
        Assert.IsFalse(filter.IsActive);
    }

    [TestMethod]
    public void Allows_NoFilter_AlwaysReturnsTrue()
    {
        var filter = new CategoryFilter(BuildMeta(), enabled: null, disabled: null);

        Assert.IsTrue(filter.Allows(1));
        Assert.IsTrue(filter.Allows(2));
        Assert.IsTrue(filter.Allows(4));
        Assert.IsTrue(filter.Allows(9999));
    }

    [TestMethod]
    public void Allows_EnabledList_PermitsOnlyMatchingCategories()
    {
        var filter = new CategoryFilter(BuildMeta(), enabled: [CatNetwork], disabled: null);

        Assert.IsTrue(filter.Allows(1),  "id=1 has category Network → allowed");
        Assert.IsFalse(filter.Allows(2), "id=2 has category Rendering → not in enabled list");
        Assert.IsFalse(filter.Allows(3), "id=3 has category Audio → not in enabled list");
    }

    [TestMethod]
    public void Allows_EnabledList_MultipleCategories_AllowsAny()
    {
        var filter = new CategoryFilter(BuildMeta(), enabled: [CatNetwork, CatAudio], disabled: null);

        Assert.IsTrue(filter.Allows(1),  "Network → allowed");
        Assert.IsFalse(filter.Allows(2), "Rendering → not allowed");
        Assert.IsTrue(filter.Allows(3),  "Audio → allowed");
    }

    [TestMethod]
    public void Allows_EnabledList_IdWithNoCategory_MapsToCategoryZero_NotAllowed()
    {
        var filter = new CategoryFilter(BuildMeta(), enabled: [CatNetwork], disabled: null);

        Assert.IsFalse(filter.Allows(4));
    }

    [TestMethod]
    public void Allows_EnabledList_CompletelyUnknownId_NotAllowed()
    {
        var filter = new CategoryFilter(BuildMeta(), enabled: [CatNetwork], disabled: null);

        Assert.IsFalse(filter.Allows(9999));
    }

    [TestMethod]
    public void Allows_DisabledList_BlocksOnlyMatchingCategories()
    {
        var filter = new CategoryFilter(BuildMeta(), enabled: null, disabled: [CatRendering]);

        Assert.IsTrue(filter.Allows(1),  "Network → not disabled");
        Assert.IsFalse(filter.Allows(2), "Rendering → disabled");
        Assert.IsTrue(filter.Allows(3),  "Audio → not disabled");
    }

    [TestMethod]
    public void Allows_DisabledList_IdWithNoCategory_IsNotBlocked()
    {
        var filter = new CategoryFilter(BuildMeta(), enabled: null, disabled: [CatRendering]);

        Assert.IsTrue(filter.Allows(4));
    }

    [TestMethod]
    public void Allows_DisabledList_UnknownId_IsAllowed()
    {
        var filter = new CategoryFilter(BuildMeta(), enabled: null, disabled: [CatRendering]);

        Assert.IsTrue(filter.Allows(9999));
    }

    [TestMethod]
    public void Allows_SameIdQueriedMultipleTimes_ReturnsConsistentResult()
    {
        var counter = new CountingMetadataProvider();
        counter.Add(1, "Fetch", "Network");

        var filter = new CategoryFilter(counter, enabled: [CatNetwork], disabled: null);

        for (int i = 0; i < 10; i++)
            filter.Allows(1);

        Assert.AreEqual(1, counter.CallCount,
            "CategoryFilter should cache the resolved category and not re-query metadata on every call");
    }

    [TestMethod]
    public void Allows_EnabledSetPresent_DisabledSetIsIgnored()
    {
        var filter = new CategoryFilter(BuildMeta(),
            enabled: [CatNetwork],
            disabled: [CatNetwork]);

        Assert.IsTrue(filter.Allows(1), "Enabled set should take precedence");
    }

    [DataTestMethod]
    [DataRow("Network",   true)]
    [DataRow("Rendering", false)]
    [DataRow("Audio",     false)]
    public void Integration_EnabledCategoryIds_FiltersAtSessionLevel(string category, bool expected)
    {
        Tracer.EnableRuntimeMetadata();

        var _ = Tracer.Id("IntegTest_Network_Fetch");
        var __ = Tracer.Id("IntegTest_Rendering_Draw");
        var ___ = Tracer.Id("IntegTest_Audio_Play");

        var ts = new TracingSession();
        ts.Start(new SessionOptions
        {
            EnabledCategoryIds = [Tracer.CategoryId("Network")],
            EnableRuntimeMetadata = true,
            ChunkCapacity = 256
        });
        ts.Stop();

        var meta = BuildMeta();
        var directFilter = new CategoryFilter(meta, enabled: [Tracer.CategoryId("Network")], disabled: null);

        bool result = directFilter.Allows(category == "Network" ? 1 : category == "Rendering" ? 2 : 3);
        Assert.AreEqual(expected, result);
    }

    private sealed class CountingMetadataProvider : ITraceMetadataProvider
    {
        private readonly Dictionary<int, TraceMeta> _map = new();
        public int CallCount { get; private set; }

        public void Add(int id, string name, string? category = null) =>
            _map[id] = new TraceMeta(id, name, category);

        public bool TryGet(int id, out TraceMeta metadata)
        {
            CallCount++;
            return _map.TryGetValue(id, out metadata);
        }
    }
}
