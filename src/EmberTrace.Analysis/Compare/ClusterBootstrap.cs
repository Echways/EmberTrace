namespace EmberTrace.Analysis.Compare;

internal static class ClusterBootstrap
{
    public static (double LowPercent, double HighPercent) RatioInterval(
        IReadOnlyList<IReadOnlyList<double>> roundsA,
        IReadOnlyList<IReadOnlyList<double>> roundsB,
        int iterations,
        int seed,
        double confidenceLevel)
    {
        var random = new Random(seed);
        var ratios = new List<double>(iterations);
        var bufferA = new List<double>();
        var bufferB = new List<double>();

        for (var i = 0; i < iterations; i++)
        {
            Resample(roundsA, random, bufferA);
            Resample(roundsB, random, bufferB);

            var baseline = Median(bufferA);
            if (baseline == 0)
                continue;

            ratios.Add((Median(bufferB) / baseline - 1) * 100);
        }

        if (ratios.Count == 0)
            return (double.NaN, double.NaN);

        ratios.Sort();
        var tail = (1 - confidenceLevel) / 2;
        var low = ratios[(int)Math.Floor(tail * (ratios.Count - 1))];
        var high = ratios[(int)Math.Ceiling((1 - tail) * (ratios.Count - 1))];
        return (low, high);
    }

    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return 0;

        var sorted = values.Order().ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static void Resample(IReadOnlyList<IReadOnlyList<double>> rounds, Random random, List<double> into)
    {
        into.Clear();
        for (var r = 0; r < rounds.Count; r++)
        {
            var round = rounds[random.Next(rounds.Count)];
            for (var p = 0; p < round.Count; p++)
                into.Add(round[random.Next(round.Count)]);
        }
    }
}