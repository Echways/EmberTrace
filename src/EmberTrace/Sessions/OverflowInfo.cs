namespace EmberTrace.Sessions;

public enum OverflowReason
{
    MaxTotalEvents = 0,
    MaxTotalChunks = 1,
    RateLimit = 2
}

public readonly record struct OverflowInfo(OverflowReason Reason, OverflowPolicy Policy);

public readonly record struct MismatchedEndInfo(int ThreadId, int ExpectedId, int ActualId, long Timestamp);
