using EmberTrace.Sessions;

namespace EmberTrace.Tests.Analysis;

[TestClass]
public class PercentileStatsTests
{
    [TestMethod]
    public void Analyze_PopulatesPercentiles()
    {
        var events = new List<TraceEventRecord>();
        long ts = 0;
        long sequence = 0;

        for (var i = 0; i < 99; i++)
        {
            events.Add(new TraceEventRecord(1, 1, ts, TraceEventKind.Begin, 0, 0, ++sequence, 1));
            ts += 1_000;
            events.Add(new TraceEventRecord(1, 1, ts, TraceEventKind.End, 0, 0, ++sequence, 1));
            ts += 1_000;
        }

        events.Add(new TraceEventRecord(1, 1, ts, TraceEventKind.Begin, 0, 0, ++sequence, 1));
        ts += 100_000;
        events.Add(new TraceEventRecord(1, 1, ts, TraceEventKind.End, 0, 0, ++sequence, 1));

        var stats = TraceSession.FromEvents(events, 0, ts, 1_000_000).Analyze();
        var row = stats.ByTotalTimeDesc.Single(r => r.Id == 1);

        Assert.AreEqual(100, row.Count);
        Assert.IsNotNull(row.Durations);
        Assert.AreEqual(100, row.Durations.Count);

        Assert.AreEqual(1.0, row.P50Ms, 0.05);
        Assert.AreEqual(1.0, row.P95Ms, 0.05);
        Assert.AreEqual(1.0, row.P99Ms, 0.05);
        Assert.AreEqual(100.0, row.MaxMs, 0.05);
    }

    [TestMethod]
    public void Analyze_PercentilesAreMonotonic()
    {
        var events = new List<TraceEventRecord>();
        long ts = 0;
        long sequence = 0;

        for (var i = 1; i <= 200; i++)
        {
            events.Add(new TraceEventRecord(1, 1, ts, TraceEventKind.Begin, 0, 0, ++sequence, 1));
            ts += i * 1_000L;
            events.Add(new TraceEventRecord(1, 1, ts, TraceEventKind.End, 0, 0, ++sequence, 1));
            ts += 1_000;
        }

        var stats = TraceSession.FromEvents(events, 0, ts, 1_000_000).Analyze();
        var row = stats.ByTotalTimeDesc.Single(r => r.Id == 1);

        Assert.IsTrue(row.MinMs <= row.P50Ms, "min must not exceed p50");
        Assert.IsTrue(row.P50Ms <= row.P90Ms, "p50 must not exceed p90");
        Assert.IsTrue(row.P90Ms <= row.P95Ms, "p90 must not exceed p95");
        Assert.IsTrue(row.P95Ms <= row.P99Ms, "p95 must not exceed p99");
        Assert.IsTrue(row.P99Ms <= row.MaxMs, "p99 must not exceed max");
    }

    [TestMethod]
    public void Process_PopulatesHotspotPercentiles()
    {
        var events = new List<TraceEventRecord>();
        long ts = 0;
        long sequence = 0;

        for (var i = 0; i < 50; i++)
        {
            events.Add(new TraceEventRecord(1, 1, ts, TraceEventKind.Begin, 0, 0, ++sequence, 1));
            ts += 2_000;
            events.Add(new TraceEventRecord(1, 1, ts, TraceEventKind.End, 0, 0, ++sequence, 1));
            ts += 1_000;
        }

        var processed = TraceSession.FromEvents(events, 0, ts, 1_000_000).Process();
        var hotspot = processed.HotspotsByInclusiveDesc.Single(h => h.Id == 1);

        Assert.AreEqual(50, hotspot.Count);
        Assert.IsNotNull(hotspot.Durations);
        Assert.AreEqual(2.0, hotspot.P50Ms, 0.1);
        Assert.AreEqual(2.0, hotspot.P95Ms, 0.1);
        Assert.AreEqual(2.0, hotspot.P99Ms, 0.1);
    }
}
