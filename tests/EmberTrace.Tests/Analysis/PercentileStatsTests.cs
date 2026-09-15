namespace EmberTrace.Tests.Analysis;

[TestClass]
public class PercentileStatsTests
{
    [TestMethod]
    public void Analyze_PercentilesIgnoreASingleOutlierWhileMaxKeepsIt()
    {
        var script = new TraceScript();
        for (var i = 0; i < 99; i++)
            script.Span(1, i * 2_000L, i * 2_000L + 1_000);

        script.Span(1, 200_000, 300_000);

        var row = script.ToSession().Analyze().ByTotalTimeDesc.Single();

        Assert.AreEqual(100L, row.Count);
        Assert.AreEqual(100L, row.Durations!.Count);
        Assert.AreEqual(1.0, row.P50Ms, 0.05);
        Assert.AreEqual(1.0, row.P90Ms, 0.05);
        Assert.AreEqual(1.0, row.P95Ms, 0.05);
        Assert.AreEqual(1.0, row.P99Ms, 0.05);
        Assert.AreEqual(100.0, row.MaxMs, 1e-9);
    }

    [TestMethod]
    public void Analyze_PercentilesAreMonotonicAndBoundedByMinAndMax()
    {
        var script = new TraceScript();
        long ts = 0;
        for (var i = 1; i <= 200; i++)
        {
            script.Span(1, ts, ts + i * 1_000L);
            ts += i * 1_000L + 1_000;
        }

        var row = script.ToSession().Analyze().ByTotalTimeDesc.Single();

        var ordered = new[] { row.MinMs, row.P50Ms, row.P90Ms, row.P95Ms, row.P99Ms, row.MaxMs };
        CollectionAssert.AreEqual(ordered.Order().ToArray(), ordered);
        Assert.AreEqual(100.0, row.P50Ms, 100.0 * 0.0313);
        Assert.AreEqual(198.0, row.P99Ms, 198.0 * 0.0313);
    }

    [TestMethod]
    public void Process_PopulatesHotspotPercentilesFromInclusiveDurations()
    {
        var script = new TraceScript();
        for (var i = 0; i < 50; i++)
            script.Begin(1, i * 3_000L).Span(2, i * 3_000L, i * 3_000L + 500).End(1, i * 3_000L + 2_000);

        var hotspot = script.ToSession().Process().HotspotsByInclusiveDesc.First(h => h.Id == 1);

        Assert.AreEqual(50L, hotspot.Count);
        Assert.AreEqual(50L, hotspot.Durations!.Count);
        Assert.AreEqual(2.0, hotspot.P50Ms, 0.07);
        Assert.AreEqual(2.0, hotspot.P95Ms, 0.07);
        Assert.AreEqual(2.0, hotspot.P99Ms, 0.07);
    }

    [TestMethod]
    public void Histograms_CarryTheSessionFrequency()
    {
        var session = new TraceScript().Span(1, 0, 2_500).ToSession();

        var row = session.Analyze().ByTotalTimeDesc.Single();
        var hotspot = session.Process().HotspotsByInclusiveDesc.Single();

        Assert.AreEqual(1_000_000L, row.Durations!.TimestampFrequency);
        Assert.AreEqual(1_000_000L, hotspot.Durations!.TimestampFrequency);
        Assert.AreEqual(2.5, row.MinMs, 1e-9);
        Assert.AreEqual(2.5, row.MaxMs, 1e-9);
        Assert.AreEqual(2.5, row.P95Ms, 1e-9);
        Assert.AreEqual(2.5, hotspot.P95Ms, 1e-9);
    }
}
