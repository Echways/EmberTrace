using EmberTrace.Internal.Time;

namespace EmberTrace.Analysis.Stats;

public sealed record DistributionShape(
    double P25Ms,
    double P75Ms,
    double MeanMs,
    double BimodalityCoefficient,
    bool IsBimodal,
    bool MedianReliable)
{
    private const int MinSamplesForBimodality = 8;
    private const double SarleThreshold = 5.0 / 9.0;
    private const double MaxMeanMedianGap = 0.25;

    internal static DistributionShape FromSortedTicks(ReadOnlySpan<long> sorted, TickConverter conv)
    {
        var count = sorted.Length;
        var mean = Mean(sorted);
        var median = SampleStatistics.PercentileTicks(sorted, 50);

        var coefficient = count >= MinSamplesForBimodality ? Sarle(sorted, mean) : 0;
        var isBimodal = coefficient > SarleThreshold;
        var reliable = count < MinSamplesForBimodality || (!isBimodal && MeanIsCloseToMedian(mean, median));

        return new DistributionShape(
            conv.ToMs(SampleStatistics.PercentileTicks(sorted, 25)),
            conv.ToMs(SampleStatistics.PercentileTicks(sorted, 75)),
            mean * conv.ToMs(1),
            coefficient,
            isBimodal,
            reliable);
    }

    private static double Mean(ReadOnlySpan<long> values)
    {
        double sum = 0;
        foreach (var value in values)
            sum += value;

        return values.IsEmpty ? 0 : sum / values.Length;
    }

    private static bool MeanIsCloseToMedian(double mean, long median)
    {
        if (median == 0)
            return mean == 0;

        return Math.Abs(mean - median) / median <= MaxMeanMedianGap;
    }

    private static double Sarle(ReadOnlySpan<long> values, double mean)
    {
        double m2 = 0, m3 = 0, m4 = 0;
        foreach (var value in values)
        {
            var d = value - mean;
            var d2 = d * d;
            m2 += d2;
            m3 += d2 * d;
            m4 += d2 * d2;
        }

        double n = values.Length;
        m2 /= n;
        m3 /= n;
        m4 /= n;

        if (m2 <= 0)
            return 0;

        var skewness = m3 / Math.Pow(m2, 1.5);
        var excessKurtosis = m4 / (m2 * m2) - 3;

        var correctedSkewness = skewness * Math.Sqrt(n * (n - 1)) / (n - 2);
        var correctedKurtosis = (n - 1) / ((n - 2) * (n - 3)) * ((n + 1) * excessKurtosis + 6);
        var denominator = correctedKurtosis + 3 * (n - 1) * (n - 1) / ((n - 2) * (n - 3));

        var coefficient = (correctedSkewness * correctedSkewness + 1) / denominator;
        return double.IsFinite(coefficient) && denominator > 0 ? coefficient : 0;
    }
}