using EmberTrace.Analysis.Stats;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.Analysis.Compare;

public sealed class RunSetComparison
{
    private const int MinimumRunsPerArm = 3;

    private RunSetComparison(
        IReadOnlyList<ScopeVerdict> scopes,
        IReadOnlyList<EnvironmentMismatch> mismatches,
        IReadOnlyList<ExcludedRun> excludedRuns)
    {
        Scopes = scopes;
        Mismatches = mismatches;
        ExcludedRuns = excludedRuns;
    }

    public IReadOnlyList<ScopeVerdict> Scopes { get; }
    public IReadOnlyList<EnvironmentMismatch> Mismatches { get; }
    public IReadOnlyList<ExcludedRun> ExcludedRuns { get; }

    public bool HasBlockingMismatch => Mismatches.Any(m => m.Severity == MismatchSeverity.Blocking);

    public static RunSetComparison Compare(
        IReadOnlyList<TraceRun> a,
        IReadOnlyList<TraceRun> b,
        ComparisonOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var settings = options ?? new ComparisonOptions();
        Validate(settings);
        var excluded = new List<ExcludedRun>();
        var keptA = Filter(a, settings, excluded);
        var keptB = Filter(b, settings, excluded);

        var mismatches = FindMismatches(keptA.Concat(keptB).Select(r => r.Run).ToList());
        var environmentReason = settings.RequireCompatibleEnvironment
            ? BlockingReason(mismatches)
            : null;

        var ids = keptA.Concat(keptB)
            .SelectMany(r => r.ById.Keys)
            .Distinct()
            .Order();

        var verdicts = ids
            .Select(id => Judge(id, keptA, keptB, settings, environmentReason))
            .ToList();

        return new RunSetComparison(verdicts, mismatches, excluded);
    }

    private static void Validate(ComparisonOptions options)
    {
        if (!(options.ConfidenceLevel > 0 && options.ConfidenceLevel < 1))
            throw new ArgumentOutOfRangeException(nameof(options), options.ConfidenceLevel,
                "ConfidenceLevel must be between 0 and 1 exclusive, for example 0.95.");

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.BootstrapIterations, nameof(options));

        if (!(options.ChangeThresholdPercent >= 0))
            throw new ArgumentOutOfRangeException(nameof(options), options.ChangeThresholdPercent,
                "ChangeThresholdPercent must not be negative.");
    }

    private static List<IndexedRun> Filter(IReadOnlyList<TraceRun> runs, ComparisonOptions options, List<ExcludedRun> excluded)
    {
        var kept = new List<IndexedRun>(runs.Count);
        foreach (var run in runs)
        {
            var reason = options.ExcludeOverflowedRuns ? ExclusionReason(run) : null;
            if (reason is null)
                kept.Add(new IndexedRun(run));
            else
                excluded.Add(new ExcludedRun(run.Label, run.Arm, reason));
        }

        return kept;
    }

    private static string? ExclusionReason(TraceRun run)
    {
        if (run.WasOverflow)
            return "the session overflowed its buffer";

        return run.DroppedEvents > 0 ? $"{run.DroppedEvents} events were dropped" : null;
    }

    private static List<EnvironmentMismatch> FindMismatches(IReadOnlyList<TraceRun> runs)
    {
        var reference = runs.Select(r => r.Environment).FirstOrDefault(e => e is not null);
        var found = new List<EnvironmentMismatch>();
        var seen = new HashSet<EnvironmentMismatch>();

        foreach (var run in runs)
            foreach (var mismatch in EnvironmentCompatibility.Compare(reference, run.Environment))
                if (seen.Add(mismatch))
                    found.Add(mismatch);

        return found;
    }

    private static string? BlockingReason(IReadOnlyList<EnvironmentMismatch> mismatches)
    {
        var blocking = mismatches.Where(m => m.Severity == MismatchSeverity.Blocking).Select(m => m.Key).Distinct().ToList();
        return blocking.Count == 0 ? null : "incompatible environment: " + string.Join(", ", blocking) + " differ";
    }

    private static ScopeVerdict Judge(
        int id,
        IReadOnlyList<IndexedRun> a,
        IReadOnlyList<IndexedRun> b,
        ComparisonOptions options,
        string? environmentReason)
    {
        var rowsA = Rows(a, id);
        var rowsB = Rows(b, id);
        var name = NameOf(id, a.Concat(b).Select(r => r.Run));

        ScopeVerdict Inconclusive(string reason, double change = 0, double low = 0, double high = 0, double p = 1)
        {
            return new ScopeVerdict(id, name, ScopeChange.Inconclusive, change, low, high, p, rowsA.Count, rowsB.Count, reason);
        }

        if (rowsA.Count == 0)
            return Inconclusive("scope present only in B (new)");
        if (rowsB.Count == 0)
            return Inconclusive("scope present only in A (gone)");

        if (rowsA.Count < MinimumRunsPerArm || rowsB.Count < MinimumRunsPerArm)
            return Inconclusive(
                $"fewer than {MinimumRunsPerArm} runs per arm (A: {rowsA.Count}, B: {rowsB.Count})");

        if (environmentReason is not null)
            return Inconclusive(environmentReason);

        var valuesA = rowsA.Select(r => r.Value).ToList();
        var valuesB = rowsB.Select(r => r.Value).ToList();
        var baseline = ClusterBootstrap.Median(valuesA);
        if (baseline == 0)
            return Inconclusive("the baseline value is zero");

        var change = (ClusterBootstrap.Median(valuesB) / baseline - 1) * 100;
        var p = MannWhitney.TwoSidedP(valuesA, valuesB);
        var (low, high) = ClusterBootstrap.RatioInterval(
            Group(rowsA), Group(rowsB), options.BootstrapIterations, options.Seed ^ id, options.ConfidenceLevel);

        if (double.IsNaN(low))
            return Inconclusive("the baseline value is zero in every resample", change, p: p);

        if (options.Statistic == Statistic.Median)
        {
            var unreliable = UnreliableMedian(rowsA, "A") ?? UnreliableMedian(rowsB, "B");
            if (unreliable is not null)
                return Inconclusive(unreliable, change, low, high, p);
        }

        var threshold = options.ChangeThresholdPercent;
        var verdict = low > threshold ? ScopeChange.Regressed
            : high < -threshold ? ScopeChange.Improved
            : low >= -threshold && high <= threshold ? ScopeChange.Unchanged
            : ScopeChange.Inconclusive;

        var reason = verdict == ScopeChange.Inconclusive
            ? "the confidence interval straddles the change threshold"
            : null;

        return new ScopeVerdict(id, name, verdict, change, low, high, p, rowsA.Count, rowsB.Count, reason);

        List<(int Round, double Value, TraceIdStats Row)> Rows(IReadOnlyList<IndexedRun> runs, int scope)
        {
            var rows = new List<(int, double, TraceIdStats)>();
            foreach (var run in runs)
            {
                if (run.ById.TryGetValue(scope, out var row))
                    rows.Add((run.Run.Round, options.Statistic == Statistic.Median ? row.P50Ms : row.AverageMs, row));
            }

            return rows;
        }
    }

    private static string? UnreliableMedian(IReadOnlyList<(int Round, double Value, TraceIdStats Row)> rows, string arm)
    {
        var unreliable = rows.Count(r => r.Row.Shape is { MedianReliable: false });
        return unreliable * 2 > rows.Count
            ? $"median unreliable: bimodal or skewed durations in {unreliable} of {rows.Count} runs of {arm}"
            : null;
    }

    private static IReadOnlyList<IReadOnlyList<double>> Group(IReadOnlyList<(int Round, double Value, TraceIdStats Row)> rows)
    {
        return rows
            .GroupBy(r => r.Round)
            .Select(g => (IReadOnlyList<double>)g.Select(r => r.Value).ToList())
            .ToList();
    }

    private static string? NameOf(int id, IEnumerable<TraceRun> runs)
    {
        foreach (var run in runs)
            if (run.Metadata is { } metadata && metadata.TryGet(id, out var meta))
                return meta.Name;

        return null;
    }

    private sealed class IndexedRun(TraceRun run)
    {
        public TraceRun Run { get; } = run;

        public IReadOnlyDictionary<int, TraceIdStats> ById { get; } = Index(run.Stats.ByTotalTimeDesc);

        private static Dictionary<int, TraceIdStats> Index(IReadOnlyList<TraceIdStats> rows)
        {
            var byId = new Dictionary<int, TraceIdStats>(rows.Count);
            foreach (var row in rows)
                byId.TryAdd(row.Id, row);

            return byId;
        }
    }
}
