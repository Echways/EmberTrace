using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Sessions;

namespace EmberTrace.Extensions.Hosting.Tests.Configuration;

[TestClass]
[DoNotParallelize]
public sealed class SessionOptionsFactoryTests
{
    [TestMethod]
    public void Defaults_AreFlightRecorderShaped()
    {
        var session = SessionOptionsFactory.Create(new EmberTraceOptions());

        Assert.AreEqual(OverflowPolicy.DropOldest, session.OverflowPolicy);
        Assert.AreEqual(TimeSpan.FromSeconds(30), session.MaxRetentionWindow);
        Assert.AreEqual(256, session.MaxTotalChunks);
        Assert.IsTrue(session.EnableRuntimeMetadata);
    }

    [TestMethod]
    public void ScalarOptions_AreCopied()
    {
        var options = new EmberTraceOptions
        {
            ChunkCapacity = 4096,
            MaxTotalEvents = 1_000_000,
            MaxTotalChunks = 32,
            SampleEveryNGlobal = 4,
            MaxEventsPerSecond = 5000,
            RuntimeCounters = RuntimeCounters.Gc | RuntimeCounters.Memory,
            RuntimeCounterInterval = TimeSpan.FromMilliseconds(200)
        };

        var session = SessionOptionsFactory.Create(options);

        Assert.AreEqual(4096, session.ChunkCapacity);
        Assert.AreEqual(1_000_000L, session.MaxTotalEvents);
        Assert.AreEqual(32, session.MaxTotalChunks);
        Assert.AreEqual(4, session.SampleEveryNGlobal);
        Assert.AreEqual(5000, session.MaxEventsPerSecond);
        Assert.AreEqual(RuntimeCounters.Gc | RuntimeCounters.Memory, session.RuntimeCounters);
        Assert.AreEqual(TimeSpan.FromMilliseconds(200), session.RuntimeCounterInterval);
    }

    [TestMethod]
    public void CategoryNames_BecomeCategoryIds()
    {
        var options = new EmberTraceOptions
        {
            EnabledCategories = ["Http", "Db"],
            DisabledCategories = ["Noise"]
        };

        var session = SessionOptionsFactory.Create(options);

        CollectionAssert.AreEqual(
            new[] { Tracer.CategoryId("Http"), Tracer.CategoryId("Db") },
            session.EnabledCategoryIds);
        CollectionAssert.AreEqual(new[] { Tracer.CategoryId("Noise") }, session.DisabledCategoryIds);
    }

    [TestMethod]
    public void EmptyCategoryLists_BecomeNull()
    {
        var session = SessionOptionsFactory.Create(new EmberTraceOptions());

        Assert.IsNull(session.EnabledCategoryIds);
        Assert.IsNull(session.DisabledCategoryIds);
    }

    [TestMethod]
    public void BlankCategoryNames_AreIgnored()
    {
        var options = new EmberTraceOptions { EnabledCategories = ["Http", "  ", ""] };

        var session = SessionOptionsFactory.Create(options);

        Assert.AreEqual(1, session.EnabledCategoryIds!.Length);
    }

    [TestMethod]
    public void ProducedOptions_AreAcceptedByTheProfiler()
    {
        var session = SessionOptionsFactory.Create(new EmberTraceOptions());

        using var tracing = new TracingSession();
        tracing.Start(session);
        var stopped = tracing.Stop();

        Assert.AreEqual(TimeSpan.FromSeconds(30), stopped.Options.MaxRetentionWindow);
    }
}
