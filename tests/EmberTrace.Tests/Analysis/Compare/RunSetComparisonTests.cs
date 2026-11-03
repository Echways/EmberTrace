using EmberTrace.Analysis.Compare;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Analysis.Compare;

[TestClass]
public class RunSetComparisonTests
{
    private const int Scope = 1;

    [TestMethod]
    public void Compare_IdenticalArms_IsUnchanged()
    {
        var verdict = Compare(Arm("A", 1_000), Arm("B", 1_000)).Scopes.Single();

        Assert.AreEqual(ScopeChange.Unchanged, verdict.Change, verdict.Reason);
        Assert.AreEqual(6, verdict.RunsA);
        Assert.AreEqual(6, verdict.RunsB);
        Assert.IsLessThan(5, verdict.CiHighPercent);
        Assert.IsGreaterThan(-5, verdict.CiLowPercent);
    }

    [TestMethod]
    public void Compare_TwentyPercentSlower_IsRegressed_WithCiBeyondThreshold()
    {
        var verdict = Compare(Arm("A", 1_000), Arm("B", 1_200)).Scopes.Single();

        Assert.AreEqual(ScopeChange.Regressed, verdict.Change);
        Assert.AreEqual(20, verdict.ChangePercent, 2);
        Assert.IsGreaterThan(5, verdict.CiLowPercent);
        Assert.IsLessThan(0.05, verdict.MannWhitneyP);
    }

    [TestMethod]
    public void Compare_TwentyPercentFaster_IsImproved()
    {
        var verdict = Compare(Arm("A", 1_000), Arm("B", 800)).Scopes.Single();

        Assert.AreEqual(ScopeChange.Improved, verdict.Change);
        Assert.IsLessThan(-5, verdict.CiHighPercent);
    }

    [TestMethod]
    public void Compare_SameInputsSameSeed_IsDeterministic()
    {
        var a = Arm("A", 1_000);
        var b = Arm("B", 1_030);

        var first = Compare(a, b).Scopes.Single();
        var second = Compare(a, b).Scopes.Single();

        Assert.AreEqual(first.CiLowPercent, second.CiLowPercent);
        Assert.AreEqual(first.CiHighPercent, second.CiHighPercent);
    }

    [TestMethod]
    public void Compare_DifferentSeeds_ResampleDifferently()
    {
        var a = Arm("A", 1_000);
        var b = Arm("B", 1_030);

        var intervals = Enumerable.Range(1, 10)
            .Select(seed => Compare(a, b, new ComparisonOptions { Seed = seed }).Scopes.Single())
            .Select(v => (v.CiLowPercent, v.CiHighPercent))
            .Distinct();

        Assert.IsGreaterThan(1, intervals.Count());
    }

    [TestMethod]
    public void Compare_Interval_ContainsThePointEstimate()
    {
        var verdict = Compare(Arm("A", 1_000), Arm("B", 1_100)).Scopes.Single();

        Assert.IsLessThanOrEqualTo(verdict.ChangePercent, verdict.CiLowPercent);
        Assert.IsGreaterThanOrEqualTo(verdict.ChangePercent, verdict.CiHighPercent);
    }

    [TestMethod]
    public void Compare_TwoProcessesPerArm_IsInconclusiveWithReason()
    {
        var verdict = Compare(Arm("A", 1_000, 1), Arm("B", 1_500, 1)).Scopes.Single();

        Assert.AreEqual(ScopeChange.Inconclusive, verdict.Change);
        StringAssert.Contains(verdict.Reason, "3");
        Assert.AreEqual(2, verdict.RunsA);
    }

    [TestMethod]
    public void Compare_DisjointScopes_ReportsNewAndGoneWithoutThrowing()
    {
        var a = Arm("A", 1_000, id: 1);
        var b = Arm("B", 1_000, id: 2);

        var scopes = Compare(a, b).Scopes;

        Assert.HasCount(2, scopes);
        Assert.IsTrue(scopes.All(s => s.Change == ScopeChange.Inconclusive));
        StringAssert.Contains(scopes.Single(s => s.Id == 1).Reason, "only in A");
        StringAssert.Contains(scopes.Single(s => s.Id == 2).Reason, "only in B");
    }

    [TestMethod]
    public void Compare_EmptyArm_ReportsEverythingAsNew()
    {
        var scopes = Compare([], Arm("B", 1_000)).Scopes;

        Assert.AreEqual(ScopeChange.Inconclusive, scopes.Single().Change);
    }

    [TestMethod]
    public void Compare_OverflowedRun_IsExcludedAndListed()
    {
        var a = Arm("A", 1_000).ToList();
        a[0] = a[0] with { WasOverflow = true };
        a[1] = a[1] with { DroppedEvents = 5 };

        var comparison = Compare(a, Arm("B", 1_000));

        Assert.HasCount(2, comparison.ExcludedRuns);
        Assert.AreEqual(4, comparison.Scopes.Single().RunsA);
        Assert.IsTrue(comparison.ExcludedRuns.All(r => r.Arm == "A"));
    }

    [TestMethod]
    public void Compare_ExcludeOverflowedRunsDisabled_KeepsThem()
    {
        var a = Arm("A", 1_000).ToList();
        a[0] = a[0] with { WasOverflow = true };

        var comparison = Compare(a, Arm("B", 1_000), new ComparisonOptions { ExcludeOverflowedRuns = false });

        Assert.IsEmpty(comparison.ExcludedRuns);
        Assert.AreEqual(6, comparison.Scopes.Single().RunsA);
    }

    [TestMethod]
    public void Compare_ExclusionLeavingTooFewRuns_IsInconclusive()
    {
        var a = Arm("A", 1_000).Select((r, i) => i < 4 ? r with { WasOverflow = true } : r).ToList();

        var verdict = Compare(a, Arm("B", 1_000)).Scopes.Single();

        Assert.AreEqual(ScopeChange.Inconclusive, verdict.Change);
        Assert.AreEqual(2, verdict.RunsA);
    }

    [TestMethod]
    public void Compare_PowersaveRoundsMixedIn_ReportsBlockingMismatch()
    {
        var a = Arm("A", 1_000).ToList();
        a[3] = a[3] with { Environment = Env("powersave") };
        a[4] = a[4] with { Environment = Env("powersave") };
        var withGovernor = a.Select((r, i) => i is 3 or 4 ? r : r with { Environment = Env("performance") }).ToList();

        var comparison = Compare(withGovernor,
            Arm("B", 1_000).Select(r => r with { Environment = Env("performance") }).ToList());

        var mismatch = comparison.Mismatches.Single(m => m.Severity == MismatchSeverity.Blocking);
        Assert.AreEqual("cpu.governor", mismatch.Key);
    }

    [TestMethod]
    public void Compare_RequireCompatibleEnvironment_BlockingMismatchMakesEverythingInconclusive()
    {
        var a = Arm("A", 1_000).Select(r => r with { Environment = Env("performance") }).ToList();
        var b = Arm("B", 1_500).Select(r => r with { Environment = Env("powersave") }).ToList();

        var comparison = Compare(a, b, new ComparisonOptions { RequireCompatibleEnvironment = true });

        var verdict = comparison.Scopes.Single();
        Assert.AreEqual(ScopeChange.Inconclusive, verdict.Change);
        StringAssert.Contains(verdict.Reason, "environment");
        Assert.IsTrue(comparison.HasBlockingMismatch);
    }

    [TestMethod]
    public void Compare_VersionOneRunAmongVersionTwo_WarnsInsteadOfThrowing()
    {
        var a = Arm("A", 1_000).Select(r => r with { Environment = Env("performance") }).ToList();
        var b = Arm("B", 1_000);

        var comparison = Compare(a, b);

        Assert.IsTrue(comparison.Mismatches.All(m => m.Severity == MismatchSeverity.Warning));
        Assert.IsNotEmpty(comparison.Mismatches);
        Assert.AreEqual(ScopeChange.Unchanged, comparison.Scopes.Single().Change);
        Assert.IsFalse(comparison.HasBlockingMismatch);
    }

    [TestMethod]
    public void Compare_ZeroBaseline_IsInconclusive()
    {
        var verdict = Compare(Arm("A", 0), Arm("B", 1_000)).Scopes.Single();

        Assert.AreEqual(ScopeChange.Inconclusive, verdict.Change);
        Assert.IsFalse(double.IsNaN(verdict.ChangePercent));
    }

    [TestMethod]
    public void Compare_BimodalScope_VerdictCarriesMedianUnreliableReason()
    {
        double[] before = [100, 100, 100, 300, 300];
        double[] after = [.. Enumerable.Repeat(138.0, 9), 300, 300];
        var a = Arm("A", 0, sampler: i => before[i % before.Length]);
        var b = Arm("B", 0, sampler: i => after[i % after.Length]);

        var median = Compare(a, b).Scopes.Single();
        var mean = Compare(a, b, new ComparisonOptions { Statistic = Statistic.Mean }).Scopes.Single();

        Assert.AreEqual(ScopeChange.Inconclusive, median.Change);
        StringAssert.Contains(median.Reason, "median unreliable");
        Assert.AreNotEqual(ScopeChange.Regressed, mean.Change);
        Assert.IsLessThan(0, mean.ChangePercent);
        Assert.IsGreaterThan(30, median.ChangePercent);
    }

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(1.0)]
    [DataRow(95.0)]
    [DataRow(-0.5)]
    [DataRow(double.NaN)]
    public void Compare_ConfidenceLevelOutsideOpenUnitInterval_Throws(double level)
    {
        var options = new ComparisonOptions { ConfidenceLevel = level };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Compare(Arm("A", 1_000), Arm("B", 1_000), options));
    }

    [TestMethod]
    public void Compare_NonPositiveBootstrapIterations_Throws()
    {
        var options = new ComparisonOptions { BootstrapIterations = 0 };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Compare(Arm("A", 1_000), Arm("B", 1_000), options));
    }

    [TestMethod]
    [DataRow(-1.0)]
    [DataRow(double.NaN)]
    public void Compare_InvalidThreshold_Throws(double threshold)
    {
        var options = new ComparisonOptions { ChangeThresholdPercent = threshold };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Compare(Arm("A", 1_000), Arm("B", 1_000), options));
    }

    [TestMethod]
    public void Compare_NullArguments_Throw()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => RunSetComparison.Compare(null!, []));
        Assert.ThrowsExactly<ArgumentNullException>(() => RunSetComparison.Compare([], null!));
    }

    [TestMethod]
    public void FromSession_TakesArmAndRoundFromTagsByDefault()
    {
        var tags = new Dictionary<string, string> { ["arm"] = "B", ["round"] = "4" };
        var session = new TraceScript().Span(Scope, 0, 1_000)
            .ToSession(environment: new TraceEnvironment(tags.ToDictionary(t => "tag." + t.Key, t => t.Value)));

        var run = TraceRun.FromSession(session, "file-1");

        Assert.AreEqual("B", run.Arm);
        Assert.AreEqual(4, run.Round);
        Assert.AreEqual("file-1", run.Label);
        Assert.IsNotNull(run.Environment);
    }

    [TestMethod]
    public void FromSession_ExplicitArgumentsWinOverTags()
    {
        var session = new TraceScript().Span(Scope, 0, 1_000)
            .ToSession(environment: new TraceEnvironment(new Dictionary<string, string>
                { ["tag.arm"] = "B", ["tag.round"] = "4" }));

        var run = TraceRun.FromSession(session, "x", "A", 9);

        Assert.AreEqual("A", run.Arm);
        Assert.AreEqual(9, run.Round);
    }

    [TestMethod]
    public void FromSession_CarriesOverflowAndDrops()
    {
        var session = TraceSession.FromEvents(
            [
                new TraceEventRecord(Scope, 1, 0, TraceEventKind.Begin, 0, 0, 1),
                new TraceEventRecord(Scope, 1, 1_000, TraceEventKind.End, 0, 0, 2)
            ],
            0, 1_000, 1_000_000, droppedEvents: 7, wasOverflow: true);

        var run = TraceRun.FromSession(session, "x", "A", 1);

        Assert.IsTrue(run.WasOverflow);
        Assert.AreEqual(7, run.DroppedEvents);
    }

    [TestMethod]
    public void FromSession_NoArmTagAndNoArgument_Throws()
    {
        var session = new TraceScript().Span(Scope, 0, 1_000).ToSession();

        var error = Assert.ThrowsExactly<ArgumentException>(() => TraceRun.FromSession(session, "x", round: 1));

        Assert.AreEqual("arm", error.ParamName);
    }

    [TestMethod]
    public void FromSession_NoRoundTagAndNoArgument_Throws()
    {
        var session = new TraceScript().Span(Scope, 0, 1_000)
            .ToSession(environment: new TraceEnvironment(new Dictionary<string, string>
                { ["tag.arm"] = "A", ["tag.round"] = "soon" }));

        var error = Assert.ThrowsExactly<ArgumentException>(() => TraceRun.FromSession(session, "x"));

        Assert.AreEqual("round", error.ParamName);
    }

    private static RunSetComparison Compare(
        IReadOnlyList<TraceRun> a, IReadOnlyList<TraceRun> b, ComparisonOptions? options = null)
    {
        return RunSetComparison.Compare(a, b, options);
    }

    private static TraceEnvironment Env(string governor)
    {
        return new TraceEnvironment(new Dictionary<string, string> { ["cpu.governor"] = governor, ["os"] = "Linux" });
    }

    private static IReadOnlyList<TraceRun> Arm(
        string arm, double meanTicks, int rounds = 3, int processesPerRound = 2, int id = Scope,
        Func<int, double>? sampler = null)
    {
        var runs = new List<TraceRun>();
        for (var round = 1; round <= rounds; round++)
        for (var process = 0; process < processesPerRound; process++)
        {
            var random = new Random(arm.Aggregate(17, (hash, c) => hash * 31 + c) * 1000 + round * 10 + process);
            var processFactor = 1 + Gaussian(random) * 0.005;
            var script = new TraceScript();
            long ts = 0;
            for (var i = 0; i < 60; i++)
            {
                var ticks = sampler is null
                    ? meanTicks * processFactor * (1 + Gaussian(random) * 0.01)
                    : sampler(i + process);
                var duration = (long)Math.Max(0, Math.Round(ticks));
                script.Span(id, ts, ts + duration);
                ts += duration + 10;
            }

            runs.Add(TraceRun.FromSession(script.ToSession(), $"{arm}-{round}-{process}", arm, round));
        }

        return runs;
    }

    private static double Gaussian(Random random)
    {
        return Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
    }
}