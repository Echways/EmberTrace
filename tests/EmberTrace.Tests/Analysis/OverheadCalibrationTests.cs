using EmberTrace.Analysis.Calibration;

namespace EmberTrace.Tests.Analysis;

[TestClass]
[DoNotParallelize]
public class OverheadCalibrationTests
{
    [TestMethod]
    public void Measure_ReturnsPositiveScopeCost_WithinSaneBounds()
    {
        var report = OverheadCalibration.Measure(iterations: 50_000);

        Assert.IsGreaterThan(0, report.ScopeNs);
        Assert.IsLessThan(10_000, report.ScopeNs);
        Assert.IsGreaterThan(0, report.TimerQuantumNs);
        Assert.IsFalse(Tracer.IsRunning);
    }

    [TestMethod]
    public void Measure_DefaultBatchSizes_AreReported()
    {
        var report = OverheadCalibration.Measure(iterations: 20_000);

        CollectionAssert.AreEquivalent(new[] { 1, 10, 100, 1000 }, report.NsPerItemByBatch.Keys.ToArray());
    }

    [TestMethod]
    public void Measure_BiggerBatches_AmortiseTheScopeCost()
    {
        var report = OverheadCalibration.Measure([1, 1000], 100_000);

        Assert.IsLessThan(report.NsPerItemByBatch[1], report.NsPerItemByBatch[1000]);
    }

    [TestMethod]
    public void Measure_WhileRunning_Throws()
    {
        Tracer.Start();
        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => OverheadCalibration.Measure(iterations: 1_000));
            Assert.IsTrue(Tracer.IsRunning);
        }
        finally
        {
            Tracer.Stop();
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-5)]
    public void Measure_NonPositiveIterations_Throws(int iterations)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => OverheadCalibration.Measure(iterations: iterations));
    }

    [TestMethod]
    public void Measure_NonPositiveBatchSize_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => OverheadCalibration.Measure([0], 1_000));
    }

    [TestMethod]
    public void MinBatchWorkNs_IsMonotonic()
    {
        var report = Report();

        Assert.IsGreaterThan(report.MinBatchWorkNs(5), report.MinBatchWorkNs(1));
        Assert.IsGreaterThan(report.MinBatchWorkNs(10), report.MinBatchWorkNs(5));
    }

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(-1.0)]
    [DataRow(double.NaN)]
    public void MinBatchWorkNs_NonPositivePercent_Throws(double percent)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Report().MinBatchWorkNs(percent));
    }

    [TestMethod]
    public void CorrectMs_NeverNegative()
    {
        var report = Report();

        Assert.AreEqual(0, report.CorrectMs(0.00001));
        Assert.AreEqual(0.9, report.CorrectMs(1.0), 1e-9);
    }

    [TestMethod]
    public void ShareOf_IsScopeCostOverMedian()
    {
        var report = Report();

        Assert.AreEqual(0.1, report.ShareOf(1.0), 1e-9);
        Assert.AreEqual(1.0, report.ShareOf(0));
    }

    [TestMethod]
    public void Report_ScopeShorterThan20xOverhead_MarkedOverheadDominated()
    {
        var script = new TraceScript()
            .Span(1, 0, 1_000)
            .Span(2, 2_000, 12_000);
        var trace = script.ToSession().Process();

        var report = TraceText.Write(trace, includePercentiles: true, overhead: Report(100_000));

        var lines = report[..report.IndexOf("Call trees", StringComparison.Ordinal)].Split(Environment.NewLine);
        Assert.Contains("overhead-dominated", lines.Single(l => l.TrimStart().StartsWith("1 ")));
        Assert.DoesNotContain("overhead-dominated", lines.Single(l => l.TrimStart().StartsWith("2 ")));
    }

    [TestMethod]
    public void Report_WithoutOverhead_HasNoNoteColumn()
    {
        var trace = new TraceScript().Span(1, 0, 1_000).ToSession().Process();

        Assert.DoesNotContain("overhead-dominated", TraceText.Write(trace));
        Assert.DoesNotContain("Note", TraceText.Write(trace));
    }

    private static OverheadReport Report(double scopeNs = 100_000)
    {
        return new OverheadReport(scopeNs, 100, new Dictionary<int, double> { [1] = scopeNs });
    }
}