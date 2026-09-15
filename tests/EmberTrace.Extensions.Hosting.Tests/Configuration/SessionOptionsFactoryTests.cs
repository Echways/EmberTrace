using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Sessions;

namespace EmberTrace.Extensions.Hosting.Tests.Configuration;

[TestClass]
public sealed class SessionOptionsFactoryTests
{
    [TestMethod]
    public void Defaults_AreFlightRecorderShapedAndAcceptedByTheProfiler()
    {
        var options = SessionOptionsFactory.Create(new EmberTraceOptions());

        Assert.AreEqual(OverflowPolicy.DropOldest, options.OverflowPolicy);
        Assert.AreEqual(TimeSpan.FromSeconds(30), options.MaxRetentionWindow);
        Assert.AreEqual(256, options.MaxTotalChunks);
        Assert.AreEqual(16_384, options.ChunkCapacity);
        Assert.IsTrue(options.EnableRuntimeMetadata);
        Assert.IsNull(options.EnabledCategoryIds);
        Assert.IsNull(options.DisabledCategoryIds);

        using var tracing = new TracingSession();
        tracing.Start(options);
        Assert.AreSame(options, tracing.Stop().Options);
    }

    [TestMethod]
    public void EveryScalarOption_IsCopied()
    {
        var options = SessionOptionsFactory.Create(new EmberTraceOptions
        {
            ChunkCapacity = 4096,
            MaxTotalEvents = 1_000_000,
            MaxTotalChunks = 32,
            MaxRetentionWindow = TimeSpan.Zero,
            OverflowPolicy = OverflowPolicy.StopSession,
            EnableRuntimeMetadata = false,
            SampleEveryNGlobal = 4,
            MaxEventsPerSecond = 5000,
            RuntimeCounters = RuntimeCounters.Gc | RuntimeCounters.Memory,
            RuntimeCounterInterval = TimeSpan.FromMilliseconds(200)
        });

        Assert.AreEqual(4096, options.ChunkCapacity);
        Assert.AreEqual(1_000_000L, options.MaxTotalEvents);
        Assert.AreEqual(32, options.MaxTotalChunks);
        Assert.AreEqual(TimeSpan.Zero, options.MaxRetentionWindow);
        Assert.AreEqual(OverflowPolicy.StopSession, options.OverflowPolicy);
        Assert.IsFalse(options.EnableRuntimeMetadata);
        Assert.AreEqual(4, options.SampleEveryNGlobal);
        Assert.AreEqual(5000, options.MaxEventsPerSecond);
        Assert.AreEqual(RuntimeCounters.Gc | RuntimeCounters.Memory, options.RuntimeCounters);
        Assert.AreEqual(TimeSpan.FromMilliseconds(200), options.RuntimeCounterInterval);
    }

    [TestMethod]
    public void CategoryNames_BecomeCategoryIdsSkippingBlanks()
    {
        var options = SessionOptionsFactory.Create(new EmberTraceOptions
        {
            EnabledCategories = ["Http", "  ", "Db"],
            DisabledCategories = ["", "Noise"]
        });

        CollectionAssert.AreEqual(new[] { Tracer.CategoryId("Http"), Tracer.CategoryId("Db") }, options.EnabledCategoryIds);
        CollectionAssert.AreEqual(new[] { Tracer.CategoryId("Noise") }, options.DisabledCategoryIds);
    }

    [TestMethod]
    public void OnlyBlankCategoryNames_BecomeNull()
    {
        var options = SessionOptionsFactory.Create(new EmberTraceOptions { EnabledCategories = ["", " "] });

        Assert.IsNull(options.EnabledCategoryIds);
    }

    [TestMethod]
    public void NullOptions_Throw()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => SessionOptionsFactory.Create(null!));
    }
}
