namespace EmberTrace.Analysis.Compare;

internal static class MannWhitney
{
    private const double SeriesLimit = 3.0;
    private const int FractionDepth = 120;

    public static double TwoSidedP(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        double n1 = x.Count;
        double n2 = y.Count;
        if (n1 == 0 || n2 == 0)
            return 1;

        var total = x.Count + y.Count;
        var pooled = new (double Value, bool FromX)[total];
        for (var i = 0; i < x.Count; i++)
            pooled[i] = (x[i], true);
        for (var i = 0; i < y.Count; i++)
            pooled[x.Count + i] = (y[i], false);

        Array.Sort(pooled, static (a, b) => a.Value.CompareTo(b.Value));

        double rankSumX = 0;
        double tieTerm = 0;
        for (var start = 0; start < total;)
        {
            var end = start;
            while (end + 1 < total && pooled[end + 1].Value == pooled[start].Value)
                end++;

            var averageRank = (start + end) / 2.0 + 1;
            var size = end - start + 1;
            tieTerm += (double)size * size * size - size;

            for (var i = start; i <= end; i++)
                if (pooled[i].FromX)
                    rankSumX += averageRank;

            start = end + 1;
        }

        var u1 = rankSumX - n1 * (n1 + 1) / 2;
        var u = Math.Max(u1, n1 * n2 - u1);
        var mean = n1 * n2 / 2;
        var variance = n1 * n2 / 12 * (total + 1 - tieTerm / ((double)total * (total - 1)));
        if (variance <= 0)
            return 1;

        var z = (u - mean - 0.5) / Math.Sqrt(variance);
        return z <= 0 ? 1 : Math.Min(1, 2 * NormalSurvival(z));
    }

    private static double NormalSurvival(double z)
    {
        var density = Math.Exp(-z * z / 2) / Math.Sqrt(2 * Math.PI);

        if (z > SeriesLimit)
        {
            var fraction = z;
            for (var k = FractionDepth; k >= 1; k--)
                fraction = z + k / fraction;

            return density / fraction;
        }

        double term = z;
        double sum = z;
        for (var n = 1; n < 200 && term > 1e-18; n++)
        {
            term *= z * z / (2 * n + 1);
            sum += term;
        }

        return 0.5 - density * sum;
    }
}
