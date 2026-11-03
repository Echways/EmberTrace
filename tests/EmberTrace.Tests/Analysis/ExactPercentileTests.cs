namespace EmberTrace.Tests.Analysis;

[TestClass]
public class ExactPercentileTests
{
    [TestMethod]
    public void Analyze_FewScopes_ReturnsExactPercentiles()
    {
        var script = new TraceScript();
        long ts = 0;
        for (var i = 0; i < 20; i++)
        {
            var duration = 1001 + i;
            script.Span(1, ts, ts + duration);
            ts += duration + 10;
        }

        var row = script.ToSession().Analyze().ByTotalTimeDesc.Single();

        Assert.IsTrue(row.IsExact);
        Assert.AreEqual(1.010, row.P50Ms, 1e-9);
        Assert.AreEqual(20, row.Samples.Length);
    }

    [TestMethod]
    public void Analyze_ExactMatchesHistogramDefinition()
    {
        var random = new Random(7);
        var script = new TraceScript();
        long ts = 0;
        for (var i = 0; i < 1_000; i++)
        {
            var duration = random.NextInt64(1_000, 50_000);
            script.Span(1, ts, ts + duration);
            ts += duration + 10;
        }

        var row = script.ToSession().Analyze().ByTotalTimeDesc.Single();
        var sorted = row.Samples.ToArray().Order().ToArray();

        foreach (var percentile in new[] { 50.0, 90.0, 95.0, 99.0 })
        {
            var exact = sorted[(int)Math.Ceiling(percentile / 100.0 * sorted.Length) - 1];
            var fromHistogram = row.Durations!.PercentileTicks(percentile);
            Assert.IsLessThanOrEqualTo(exact / 256 + 1, Math.Abs(fromHistogram - exact));
        }
    }

    [TestMethod]
    public void Analyze_SamplesKeepClosingOrder()
    {
        var script = new TraceScript()
            .Span(1, 0, 3_000)
            .Span(1, 4_000, 5_000)
            .Span(1, 6_000, 8_000);

        var row = script.ToSession().Analyze().ByTotalTimeDesc.Single();

        CollectionAssert.AreEqual(new long[] { 3_000, 1_000, 2_000 }, row.Samples.ToArray());
    }

    [TestMethod]
    public void Analyze_OverLimit_FallsBackToHistogram()
    {
        var script = new TraceScript();
        for (var i = 0; i < 50; i++)
            script.Span(1, i * 3_000L, i * 3_000L + 1_000);

        var row = script.ToSession().Analyze(maxSamplesPerId: 10).ByTotalTimeDesc.Single();

        Assert.IsFalse(row.IsExact);
        Assert.IsTrue(row.Samples.IsEmpty);
        Assert.AreEqual(1.0, row.P50Ms, 0.01);
    }

    [TestMethod]
    public void Analyze_MaxSamplesZero_NeverAllocatesSamples()
    {
        var script = new TraceScript().Span(1, 0, 1_000).Span(1, 2_000, 3_000);

        var row = script.ToSession().Analyze(maxSamplesPerId: 0).ByTotalTimeDesc.Single();

        Assert.IsFalse(row.IsExact);
        Assert.IsTrue(row.Samples.IsEmpty);
    }

    [TestMethod]
    public void Analyze_NegativeMaxSamples_Throws()
    {
        var session = new TraceScript().Span(1, 0, 1_000).ToSession();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => session.Analyze(maxSamplesPerId: -1));
    }
}