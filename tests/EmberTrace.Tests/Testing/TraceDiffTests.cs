using EmberTrace.Analysis.Stats;
using EmberTrace.Metadata;
using EmberTrace.Testing;

namespace EmberTrace.Tests.Testing;

[TestClass]
public class TraceDiffTests
{
    [TestMethod]
    public void Compare_ComputesPercentChangeForSharedIds()
    {
        var baseline = Stats((1, 100, 10.0, 1.0), (2, 50, 5.0, 0.5));
        var current = Stats((1, 100, 15.0, 1.5), (2, 50, 5.0, 0.5));

        var comparison = TraceDiff.Compare(baseline, current);

        var slower = comparison.Deltas.Single(d => d.Id == 1);
        Assert.IsTrue(slower.InBaseline);
        Assert.IsTrue(slower.InCurrent);
        Assert.AreEqual(50.0, slower.TotalMsChangePercent, 0.001);
        Assert.AreEqual(50.0, slower.P95MsChangePercent, 0.001);
        Assert.AreEqual(5.0, slower.TotalMsDelta, 0.001);

        var unchanged = comparison.Deltas.Single(d => d.Id == 2);
        Assert.AreEqual(0.0, unchanged.TotalMsChangePercent, 0.001);
    }

    [TestMethod]
    public void Compare_SortsWorstRegressionFirst()
    {
        var baseline = Stats((1, 10, 10.0, 1.0), (2, 10, 10.0, 1.0));
        var current = Stats((1, 10, 11.0, 1.0), (2, 10, 20.0, 1.0));

        var comparison = TraceDiff.Compare(baseline, current);

        Assert.AreEqual(2, comparison.Deltas[0].Id, "the worst regression must come first");
    }

    [TestMethod]
    public void Compare_FlagsIdsPresentOnOneSideOnly()
    {
        var baseline = Stats((1, 10, 10.0, 1.0));
        var current = Stats((2, 10, 10.0, 1.0));

        var comparison = TraceDiff.Compare(baseline, current);

        var removed = comparison.Deltas.Single(d => d.Id == 1);
        Assert.IsTrue(removed.InBaseline);
        Assert.IsFalse(removed.InCurrent);
        Assert.IsTrue(double.IsNaN(removed.TotalMsChangePercent));

        var added = comparison.Deltas.Single(d => d.Id == 2);
        Assert.IsFalse(added.InBaseline);
        Assert.IsTrue(added.InCurrent);

        Assert.IsEmpty(comparison.InBothOnly);
    }

    [TestMethod]
    public void RegressionsOver_ExcludesOneSidedIdsAndImprovements()
    {
        var baseline = Stats((1, 10, 10.0, 1.0), (2, 10, 10.0, 1.0));
        var current = Stats((1, 10, 30.0, 1.0), (2, 10, 5.0, 1.0), (3, 10, 99.0, 1.0));

        var comparison = TraceDiff.Compare(baseline, current);
        var regressions = comparison.RegressionsOver(10).ToList();

        Assert.HasCount(1, regressions);
        Assert.AreEqual(1, regressions[0].Id);
    }

    [TestMethod]
    public void Compare_ZeroBaseline_YieldsInfinity()
    {
        var baseline = Stats((1, 10, 0.0, 0.0));
        var current = Stats((1, 10, 5.0, 1.0));

        var comparison = TraceDiff.Compare(baseline, current);

        Assert.IsTrue(double.IsPositiveInfinity(comparison.Deltas[0].TotalMsChangePercent));
    }

    [TestMethod]
    public void Format_RendersReadableRows()
    {
        var baseline = Stats((1, 10, 10.0, 1.0));
        var current = Stats((1, 10, 20.0, 2.0));
        var meta = TraceMetadata.FromEntries(new[] { new TraceMeta(1, "Render", "CPU") });

        var text = TraceDiff.Format(TraceDiff.Compare(baseline, current), meta);

        Assert.Contains("Render", text);
        Assert.Contains("+100.0", text);
    }

    [TestMethod]
    public void Format_MarksOneSidedIdsAndHonoursMinPercent()
    {
        var baseline = Stats((1, 10, 10.0, 1.0), (2, 10, 10.0, 1.0));
        var current = Stats((1, 10, 30.0, 1.0), (3, 10, 10.0, 1.0));

        var text = TraceDiff.Format(TraceDiff.Compare(baseline, current), minPercent: 10);

        Assert.Contains("(new)", text);
        Assert.Contains("(gone)", text);
        Assert.Contains("+200.0", text);
    }

    [TestMethod]
    public void AssertNoRegressions_PassesWithinBudget()
    {
        var baseline = Stats((1, 10, 10.0, 1.0));
        var current = Stats((1, 10, 10.5, 1.0));

        TraceBudget.AssertNoRegressions(baseline, current, 10);
    }

    [TestMethod]
    public void AssertNoRegressions_ThrowsAndListsEveryOffender()
    {
        var baseline = Stats((1, 10, 10.0, 1.0), (2, 10, 10.0, 1.0));
        var current = Stats((1, 10, 30.0, 1.0), (2, 10, 40.0, 1.0));
        var meta = TraceMetadata.FromEntries(new[]
        {
            new TraceMeta(1, "Parse", "CPU"),
            new TraceMeta(2, "Render", "CPU")
        });

        var ex = Assert.ThrowsExactly<TraceAssertionException>(
            () => TraceBudget.AssertNoRegressions(baseline, current, 10, meta));

        Assert.Contains("Parse", ex.Message);
        Assert.Contains("Render", ex.Message);
        Assert.Contains("10.0%", ex.Message);
        Assert.Contains("+200.0%", ex.Message);
        Assert.Contains("+300.0%", ex.Message);
    }

    [TestMethod]
    public void AssertNoRegressions_IgnoresNewlyAddedScopes()
    {
        var baseline = Stats((1, 10, 10.0, 1.0));
        var current = Stats((1, 10, 10.0, 1.0), (2, 10, 500.0, 1.0));

        TraceBudget.AssertNoRegressions(baseline, current, 10);
    }

    private static TraceStats Stats(params (int Id, long Count, double TotalMs, double P95Ms)[] rows)
    {
        var list = new List<TraceIdStats>(rows.Length);
        foreach (var row in rows)
            list.Add(new TraceIdStats
            {
                Id = row.Id,
                Count = row.Count,
                TotalMs = row.TotalMs,
                AverageMs = row.Count == 0 ? 0 : row.TotalMs / row.Count,
                MinMs = 0,
                MaxMs = row.P95Ms,
                P50Ms = row.P95Ms,
                P90Ms = row.P95Ms,
                P95Ms = row.P95Ms,
                P99Ms = row.P95Ms
            });

        return new TraceStats
        {
            DurationMs = 1000,
            TotalEventCount = 0,
            ScopeEventCount = 0,
            ThreadsSeen = 1,
            UnmatchedBeginCount = 0,
            UnmatchedEndCount = 0,
            MismatchedEndCount = 0,
            ByTotalTimeDesc = list
        };
    }
}
