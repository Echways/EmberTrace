namespace EmberTrace.Analysis.Compare;

public sealed class ComparisonOptions
{
    public Statistic Statistic { get; init; } = Statistic.Median;
    public double ChangeThresholdPercent { get; init; } = 5;
    public double ConfidenceLevel { get; init; } = 0.95;
    public int BootstrapIterations { get; init; } = 10_000;
    public int Seed { get; init; } = 12345;
    public bool ExcludeOverflowedRuns { get; init; } = true;
    public bool RequireCompatibleEnvironment { get; init; }
}
