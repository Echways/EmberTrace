namespace EmberTrace.Sessions;

public static class EnvironmentCompatibility
{
    private const string TagPrefix = "tag.";
    private const string UnknownKey = "environment";
    private const string UnknownValue = "unknown";

    private static readonly string[] BlockingPrefixes =
        ["cpu.governor", "cpu.turbo", "runtime", "tiered.", "gc.", "cpu.affinity", "cgroup."];

    public static IReadOnlyList<EnvironmentMismatch> Compare(TraceEnvironment? a, TraceEnvironment? b)
    {
        if (a is null || b is null)
            return
            [
                new EnvironmentMismatch(
                    UnknownKey,
                    a is null ? UnknownValue : null,
                    b is null ? UnknownValue : null,
                    MismatchSeverity.Warning)
            ];

        var keys = new SortedSet<string>(a.Values.Keys.Concat(b.Values.Keys), StringComparer.Ordinal);
        var mismatches = new List<EnvironmentMismatch>();

        foreach (var key in keys)
        {
            if (key.StartsWith(TagPrefix, StringComparison.Ordinal))
                continue;

            a.Values.TryGetValue(key, out var left);
            b.Values.TryGetValue(key, out var right);
            if (string.Equals(left, right, StringComparison.Ordinal))
                continue;

            mismatches.Add(new EnvironmentMismatch(key, left, right, SeverityOf(key)));
        }

        return mismatches;
    }

    private static MismatchSeverity SeverityOf(string key)
    {
        foreach (var prefix in BlockingPrefixes)
            if (key.StartsWith(prefix, StringComparison.Ordinal))
                return MismatchSeverity.Blocking;

        return MismatchSeverity.Warning;
    }
}