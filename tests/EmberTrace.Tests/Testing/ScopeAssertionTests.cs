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
        var stats = BuildStats();

        stats.Scope(1)
            .CountExactly(100)
            .TotalMsUnder(1000)
            .AverageMsUnder(5.001)
            .P95MsUnder(6)
            .P99MsUnder(6);
    }

    [TestMethod]
    public void FailingThreshold_ThrowsWithNumbers()
    {
        var stats = BuildStats();

        var ex = Assert.ThrowsExactly<TraceAssertionException>(() => stats.Scope(1).P95MsUnder(1));

        Assert.Contains("p95", ex.Message);
        Assert.Contains("1.000", ex.Message);
        Assert.Contains("5.0", ex.Message);
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
        var stats = BuildStats();

        stats.Scope(1).CountAtMost(100).CountAtLeast(100);

        Assert.ThrowsExactly<TraceAssertionException>(() => stats.Scope(1).CountAtMost(99));
        Assert.ThrowsExactly<TraceAssertionException>(() => stats.Scope(1).CountAtLeast(101));
    }

    [TestMethod]
    public void PercentileMsUnder_MatchesTheDedicatedOverloads()
    {
        var stats = BuildStats();

        stats.Scope(1).PercentileMsUnder(99.9, 6);

        var ex = Assert.ThrowsExactly<TraceAssertionException>(() => stats.Scope(1).PercentileMsUnder(99.9, 1));
        Assert.Contains("p99.9", ex.Message);
    }

    [TestMethod]
    public void PercentileMsUnder_RejectsOutOfRangePercentiles()
    {
        var stats = BuildStats();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => stats.Scope(1).PercentileMsUnder(150, 1));
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

    private static TraceStats BuildStats()
    {
        var histogram = new DurationHistogram();
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
