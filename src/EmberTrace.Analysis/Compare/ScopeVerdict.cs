namespace EmberTrace.Analysis.Compare;

public sealed record ScopeVerdict(
    int Id,
    string? Name,
    ScopeChange Change,
    double ChangePercent,
    double CiLowPercent,
    double CiHighPercent,
    double MannWhitneyP,
    int RunsA,
    int RunsB,
    string? Reason);