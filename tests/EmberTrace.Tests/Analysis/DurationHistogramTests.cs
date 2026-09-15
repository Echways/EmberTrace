using EmberTrace.Analysis.Stats;

namespace EmberTrace.Tests.Analysis;

[TestClass]
public class DurationHistogramTests
{
    [TestMethod]
    public void Empty_ReportsZeroForEverything()
    {
        var histogram = new DurationHistogram();

        Assert.AreEqual(0, histogram.Count);
        Assert.AreEqual(0, histogram.PercentileTicks(50));
        Assert.AreEqual(0, histogram.PercentileTicks(99));
    }

    [TestMethod]
    public void SmallValues_AreExact()
    {
        var histogram = new DurationHistogram();
        for (var i = 0; i < 64; i++)
            histogram.Add(i);

        Assert.AreEqual(64, histogram.Count);
        Assert.AreEqual(0, histogram.MinTicks);
        Assert.AreEqual(63, histogram.MaxTicks);
        Assert.AreEqual(31, histogram.PercentileTicks(50));
        Assert.AreEqual(63, histogram.PercentileTicks(100));
    }

    [TestMethod]
    public void BucketIndices_AreContiguousAcrossMagnitudeBoundaries()
    {
        Assert.AreEqual(63, DurationHistogram.BucketIndexOf(63));
        Assert.AreEqual(64, DurationHistogram.BucketIndexOf(64));
        Assert.AreEqual(95, DurationHistogram.BucketIndexOf(127));
        Assert.AreEqual(96, DurationHistogram.BucketIndexOf(128));
    }

    [TestMethod]
    public void BucketIndexOf_LongMaxValue_StaysInRange()
    {
        var index = DurationHistogram.BucketIndexOf(long.MaxValue);

        Assert.AreEqual(1887, index);
        Assert.AreEqual(long.MaxValue, DurationHistogram.UpperBoundOf(index));
    }

    [TestMethod]
    public void UpperBound_IsNeverBelowTheValueItBuckets()
    {
        long value = 1;
        while (value > 0 && value < long.MaxValue / 3)
        {
            var upper = DurationHistogram.UpperBoundOf(DurationHistogram.BucketIndexOf(value));
            Assert.IsGreaterThanOrEqualTo(value, upper);
            value = value * 3 + 1;
        }
    }

    [TestMethod]
    public void LargeValues_StayWithinThreePointTwoPercent()
    {
        long value = 64;
        while (value < 1L << 40)
        {
            var reported = DurationHistogram.UpperBoundOf(DurationHistogram.BucketIndexOf(value));
            var error = (double)(reported - value) / value;

            Assert.IsLessThan(0.0313, error);
            value = (long)(value * 1.37) + 1;
        }
    }

    [TestMethod]
    public void Percentiles_TrackAKnownDistribution()
    {
        var histogram = new DurationHistogram();

        for (var i = 0; i < 99; i++)
            histogram.Add(1000);
        histogram.Add(1_000_000);

        Assert.AreEqual(100, histogram.Count);
        Assert.AreEqual(1000, histogram.MinTicks);
        Assert.AreEqual(1_000_000, histogram.MaxTicks);

        Assert.IsLessThan(1032L, histogram.PercentileTicks(50));
        Assert.IsLessThan(1032L, histogram.PercentileTicks(95));
        Assert.IsLessThan(1032L, histogram.PercentileTicks(99));
        Assert.AreEqual(1_000_000L, histogram.PercentileTicks(100));
    }

    [TestMethod]
    [DataRow(-0.001)]
    [DataRow(100.001)]
    [DataRow(double.NaN)]
    public void Percentile_OutsideZeroToHundred_Throws(double percentile)
    {
        var histogram = new DurationHistogram();
        histogram.Add(10);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => histogram.PercentileTicks(percentile));
    }

    [TestMethod]
    public void Add_NegativeTicks_Throws()
    {
        var histogram = new DurationHistogram();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => histogram.Add(-1));
    }

    [TestMethod]
    public void PercentileMs_ConvertsWithTheHistogramFrequency()
    {
        var histogram = new DurationHistogram(1_000_000);
        for (var i = 0; i < 100; i++)
            histogram.Add(5_000);

        Assert.AreEqual(1_000_000, histogram.TimestampFrequency);
        Assert.AreEqual(histogram.PercentileTicks(95) / 1_000.0, histogram.PercentileMs(95), 1e-9);
    }

    [TestMethod]
    public void PercentileMs_WithoutFrequency_Throws()
    {
        var histogram = new DurationHistogram();
        histogram.Add(5_000);

        Assert.ThrowsExactly<InvalidOperationException>(() => histogram.PercentileMs(50));
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public void Constructor_RejectsNonPositiveFrequency(long frequency)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new DurationHistogram(frequency));
    }
}
