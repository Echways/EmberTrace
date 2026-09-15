using EmberTrace.Analysis.Stats;
using EmberTrace.Metadata;
using EmberTrace.Testing;

namespace EmberTrace.Tests.Testing;

[TestClass]
public class ScopeAssertionTests
{
    [TestMethod]
    public void PassingAssertions_Chain()
    {
        BuildStats().Scope(1)
            .CountExactly(100)
            .CountAtLeast(100)
            .CountAtMost(100)
            .TotalMsUnder(500.001)
            .AverageMsUnder(5.001)
            .MaxMsUnder(5.001)
            .P50MsUnder(5.001)
            .P95MsUnder(5.001)
            .P99MsUnder(5.001)
            .PercentileMsUnder(99.9, 5.001);
    }

    [TestMethod]
    [DataRow("total", 500.0)]
    [DataRow("average", 5.0)]
    [DataRow("max", 5.0)]
    [DataRow("p50", 5.0)]
    [DataRow("p95", 5.0)]
    [DataRow("p99", 5.0)]
    public void Thresholds_AreExclusiveAndReportActualAndLimit(string metric, double limit)
    {
        var scope = BuildStats().Scope(1);

        var ex = Assert.ThrowsExactly<TraceAssertionException>(() => _ = metric switch
        {
            "total" => scope.TotalMsUnder(limit),
            "average" => scope.AverageMsUnder(limit),
            "max" => scope.MaxMsUnder(limit),
            "p50" => scope.P50MsUnder(limit),
            "p95" => scope.P95MsUnder(limit),
            _ => scope.P99MsUnder(limit)
        });

        Assert.AreEqual(
            FormattableString.Invariant(
                $"Trace assertion failed: expected id 1 {metric} to be under {limit:F3} ms, but it was {limit:F3} ms."),
            ex.Message);
    }

    [TestMethod]
    public void FailingAssertion_NamesTheScopeWhenMetadataIsAvailable()
    {
        var stats = BuildStats();
        var meta = TraceMetadata.FromEntries(new[] { new TraceMeta(1, "DbQuery", "IO") });

        var ex = Assert.ThrowsExactly<TraceAssertionException>(() => stats.Scope(1, meta).MaxMsUnder(0.001));

        Assert.Contains("DbQuery", ex.Message);
    }

    [TestMethod]
    public void MissingScope_FailsThresholdAssertions()
    {
        var stats = BuildStats();

        var ex = Assert.ThrowsExactly<TraceAssertionException>(() => stats.Scope(999).P95MsUnder(1));

        Assert.Contains("never recorded", ex.Message);
    }

    [TestMethod]
    public void MissingScope_SatisfiesNotRecorded()
    {
        var stats = BuildStats();

        stats.Scope(999).NotRecorded();
    }

    [TestMethod]
    public void PresentScope_FailsNotRecorded()
    {
        var stats = BuildStats();

        Assert.ThrowsExactly<TraceAssertionException>(() => stats.Scope(1).NotRecorded());
    }

    [TestMethod]
    public void CountBounds_AreEnforced()
    {
        var scope = BuildStats().Scope(1);

        Assert.ThrowsExactly<TraceAssertionException>(() => scope.CountExactly(99));
        Assert.ThrowsExactly<TraceAssertionException>(() => scope.CountAtMost(99));
        Assert.ThrowsExactly<TraceAssertionException>(() => scope.CountAtLeast(101));
    }

    [TestMethod]
    public void PercentileMsUnder_NamesTheRequestedPercentile()
    {
        var ex = Assert.ThrowsExactly<TraceAssertionException>(() => BuildStats().Scope(1).PercentileMsUnder(99.9, 1));

        Assert.Contains("p99.9 to be under 1.000 ms", ex.Message);
    }

    [TestMethod]
    [DataRow(-1.0)]
    [DataRow(150.0)]
    [DataRow(double.NaN)]
    public void PercentileMsUnder_RejectsOutOfRangePercentiles(double percentile)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BuildStats().Scope(1).PercentileMsUnder(percentile, 1));
    }

    [TestMethod]
    public void PercentileMsUnder_WithoutHistogram_Fails()
    {
        var stats = new TraceStats
        {
            DurationMs = 1000,
            TotalEventCount = 0,
            ScopeEventCount = 0,
            ThreadsSeen = 1,
            UnmatchedBeginCount = 0,
            UnmatchedEndCount = 0,
            MismatchedEndCount = 0,
            ByTotalTimeDesc = new[]
            {
                new TraceIdStats
                {
                    Id = 1,
                    Count = 1,
                    TotalMs = 5,
                    AverageMs = 5,
                    MinMs = 5,
                    MaxMs = 5
                }
            }
        };

        var ex = Assert.ThrowsExactly<TraceAssertionException>(() => stats.Scope(1).PercentileMsUnder(90, 1));

        Assert.Contains("Analyze()", ex.Message);
    }

    [TestMethod]
    public void PercentileMsUnder_WithHistogramWithoutFrequency_Fails()
    {
        var histogram = new DurationHistogram();
        histogram.Add(5_000);

        var stats = new TraceStats
        {
            DurationMs = 1000,
            TotalEventCount = 2,
            ScopeEventCount = 2,
            ThreadsSeen = 1,
            UnmatchedBeginCount = 0,
            UnmatchedEndCount = 0,
            MismatchedEndCount = 0,
            ByTotalTimeDesc = new[]
            {
                new TraceIdStats
                {
                    Id = 1,
                    Count = 1,
                    TotalMs = 5,
                    AverageMs = 5,
                    MinMs = 5,
                    MaxMs = 5,
                    Durations = histogram
                }
            }
        };

        var ex = Assert.ThrowsExactly<TraceAssertionException>(() => stats.Scope(1).PercentileMsUnder(90, 10));

        Assert.Contains("timestamp frequency", ex.Message);
    }

    private static TraceStats BuildStats()
    {
        var histogram = new DurationHistogram(1_000_000);
        for (var i = 0; i < 100; i++)
            histogram.Add(5_000);

        const double toMs = 1000.0 / 1_000_000;

        return new TraceStats
        {
            DurationMs = 1000,
            TotalEventCount = 200,
            ScopeEventCount = 200,
            ThreadsSeen = 1,
            UnmatchedBeginCount = 0,
            UnmatchedEndCount = 0,
            MismatchedEndCount = 0,
            ByTotalTimeDesc = new[]
            {
                new TraceIdStats
                {
                    Id = 1,
                    Count = 100,
                    TotalMs = 500,
                    AverageMs = 5,
                    MinMs = 5,
                    MaxMs = 5,
                    Durations = histogram,
                    P50Ms = histogram.PercentileTicks(50) * toMs,
                    P90Ms = histogram.PercentileTicks(90) * toMs,
                    P95Ms = histogram.PercentileTicks(95) * toMs,
                    P99Ms = histogram.PercentileTicks(99) * toMs
                }
            }
        };
    }
}
