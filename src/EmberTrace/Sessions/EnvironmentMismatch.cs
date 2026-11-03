namespace EmberTrace.Sessions;

public sealed record EnvironmentMismatch(string Key, string? A, string? B, MismatchSeverity Severity);
