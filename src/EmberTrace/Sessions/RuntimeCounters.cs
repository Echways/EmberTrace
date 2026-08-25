namespace EmberTrace.Sessions;

[Flags]
public enum RuntimeCounters
{
    None = 0,
    Gc = 1 << 0,
    Memory = 1 << 1,
    ThreadPool = 1 << 2,
    Exceptions = 1 << 3,
    GcPauses = 1 << 4,
    All = Gc | Memory | ThreadPool | Exceptions | GcPauses
}

public static class RuntimeCounterIds
{
    public const string Category = "Runtime";

    public const int GcGen0 = -1;
    public const int GcGen1 = -2;
    public const int GcGen2 = -3;
    public const int HeapBytes = -4;
    public const int AllocatedBytes = -5;
    public const int ThreadPoolThreads = -6;
    public const int ThreadPoolQueue = -7;
    public const int ThreadPoolCompleted = -8;
    public const int Exceptions = -9;
    public const int GcPause = -10;

    internal const int LowestReserved = GcPause;

    public static bool IsReserved(int id)
    {
        return id is <= -1 and >= LowestReserved;
    }
}
