namespace EmberTrace.Analysis.Calibration;

public sealed record OverheadReport(
    double ScopeNs,
    double TimerQuantumNs,
    IReadOnlyDictionary<int, double> NsPerItemByBatch)
{
    private const double DominatedShare = 0.05;

    public double CorrectMs(double measuredMs)
    {
        return Math.Max(0, measuredMs - ScopeNs / 1_000_000.0);
    }

    public double ShareOf(double medianMs)
    {
        if (medianMs <= 0)
            return 1;

        return Math.Min(1, ScopeNs / (medianMs * 1_000_000.0));
    }

    public bool IsOverheadDominated(double medianMs)
    {
        return ShareOf(medianMs) > DominatedShare;
    }

    public double MinBatchWorkNs(double maxOverheadPercent)
    {
        if (!(maxOverheadPercent > 0) || double.IsInfinity(maxOverheadPercent))
            throw new ArgumentOutOfRangeException(nameof(maxOverheadPercent), maxOverheadPercent,
                "The overhead tolerance must be a positive percentage.");

        return ScopeNs / (maxOverheadPercent / 100.0);
    }
}
