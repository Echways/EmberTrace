namespace EmberTrace.Sessions;

public sealed record TraceEnvironment(IReadOnlyDictionary<string, string> Values);