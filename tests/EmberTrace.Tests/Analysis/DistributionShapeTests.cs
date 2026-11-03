namespace EmberTrace.Tests.Analysis;

[TestClass]
public class DistributionShapeTests
{
    [TestMethod]
    public void Shape_TwoClusters_IsBimodal()
    {
        var random = new Random(3);
        var durations = Enumerable.Range(0, 1_000)
            .Select(i => (i % 2 == 0 ? 100L : 200L) + random.NextInt64(0, 3))
            .ToArray();

        var shape = Analyze(durations).Shape!;

        Assert.IsTrue(shape.IsBimodal);
        Assert.IsFalse(shape.MedianReliable);
        Assert.IsGreaterThan(5.0 / 9.0, shape.BimodalityCoefficient);
    }

    [TestMethod]
    public void Shape_SingleCluster_IsNotBimodal()
    {
        var random = new Random(5);
        var durations = Enumerable.Range(0, 1_000)
            .Select(_ => (long)Math.Max(1, 1_000 + Gaussian(random) * 30))
            .ToArray();

        var shape = Analyze(durations).Shape!;

        Assert.IsFalse(shape.IsBimodal);
        Assert.IsTrue(shape.MedianReliable);
        Assert.IsLessThan(shape.P75Ms, shape.P25Ms);
    }

    [TestMethod]
    public void Shape_FewSamples_NeverBimodal()
    {
        var shape = Analyze([100, 100, 100, 5_000, 5_000]).Shape!;

        Assert.IsFalse(shape.IsBimodal);
        Assert.IsTrue(shape.MedianReliable);
    }

    [TestMethod]
    public void Shape_AllEqual_NoNaN()
    {
        var shape = Analyze(Enumerable.Repeat(500L, 50).ToArray()).Shape!;

        Assert.IsFalse(double.IsNaN(shape.BimodalityCoefficient));
        Assert.IsFalse(shape.IsBimodal);
        Assert.IsTrue(shape.MedianReliable);
        Assert.AreEqual(0.5, shape.P25Ms, 1e-9);
    }

    [TestMethod]
    public void Shape_AllZeroDurations_NoNaN()
    {
        var shape = Analyze(Enumerable.Repeat(0L, 20).ToArray()).Shape!;

        Assert.IsFalse(double.IsNaN(shape.MeanMs));
        Assert.IsTrue(shape.MedianReliable);
    }

    [TestMethod]
    public void Shape_SingleSample_IsDefined()
    {
        var shape = Analyze([700]).Shape!;

        Assert.AreEqual(0.7, shape.P25Ms, 1e-9);
        Assert.IsFalse(shape.IsBimodal);
        Assert.IsTrue(shape.MedianReliable);
    }

    [TestMethod]
    public void Shape_MeanFarFromMedian_MedianIsUnreliable()
    {
        var durations = Enumerable.Repeat(100L, 90).Concat(Enumerable.Repeat(10_000L, 10)).ToArray();

        var shape = Analyze(durations).Shape!;

        Assert.IsFalse(shape.MedianReliable);
    }

    [TestMethod]
    public void Shape_OverSampleLimit_IsNull()
    {
        var row = Script(Enumerable.Repeat(100L, 50).ToArray()).ToSession().Analyze(maxSamplesPerId: 10)
            .ByTotalTimeDesc.Single();

        Assert.IsNull(row.Shape);
    }

    [TestMethod]
    public void Warmup_SlowThenFast_FirstWindowExceedsLast()
    {
        var durations = Enumerable.Repeat(5_000L, 100).Concat(Enumerable.Repeat(1_000L, 100)).ToArray();

        var curve = Analyze(durations).Warmup();

        Assert.HasCount(10, curve.Windows);
        Assert.AreEqual(5.0, curve.Windows[0], 1e-9);
        Assert.AreEqual(1.0, curve.Windows[^1], 1e-9);
    }

    [TestMethod]
    public void Warmup_FewerSamplesThanWindows_UsesOneWindowPerSample()
    {
        var curve = Analyze([1_000, 2_000, 3_000]).Warmup(10);

        CollectionAssert.AreEqual(new[] { 1.0, 2.0, 3.0 }, curve.Windows.ToArray());
    }

    [TestMethod]
    public void Warmup_WithoutSamples_IsEmpty()
    {
        var row = Script([100, 100]).ToSession().Analyze(maxSamplesPerId: 0).ByTotalTimeDesc.Single();

        Assert.IsEmpty(row.Warmup().Windows);
    }

    [TestMethod]
    public void Warmup_NonPositiveWindows_Throws()
    {
        var row = Analyze([100, 200]);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => row.Warmup(0));
    }

    private static TraceScript Script(long[] durations)
    {
        var script = new TraceScript();
        long ts = 0;
        foreach (var duration in durations)
        {
            script.Span(1, ts, ts + duration);
            ts += duration + 10;
        }

        return script;
    }

    private static EmberTrace.Analysis.Stats.TraceIdStats Analyze(long[] durations)
    {
        return Script(durations).ToSession().Analyze().ByTotalTimeDesc.Single();
    }

    private static double Gaussian(Random random)
    {
        return Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
    }
}
