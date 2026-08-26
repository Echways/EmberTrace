using EmberTrace.Sessions;

namespace EmberTrace.Extensions.Hosting.Configuration;

public sealed class EmberTraceOptions
{
    public const string SectionName = "EmberTrace";

    public bool Enabled { get; set; } = true;
    public int ChunkCapacity { get; set; } = 16_384;
    public long MaxTotalEvents { get; set; }
    public int MaxTotalChunks { get; set; } = 256;
    public TimeSpan MaxRetentionWindow { get; set; } = TimeSpan.FromSeconds(30);
    public OverflowPolicy OverflowPolicy { get; set; } = OverflowPolicy.DropOldest;
    public bool EnableRuntimeMetadata { get; set; } = true;
    public RuntimeCounters RuntimeCounters { get; set; } = RuntimeCounters.None;
    public TimeSpan RuntimeCounterInterval { get; set; } = TimeSpan.FromMilliseconds(50);
    public int SampleEveryNGlobal { get; set; }
    public int MaxEventsPerSecond { get; set; }
    public string[] EnabledCategories { get; set; } = [];
    public string[] DisabledCategories { get; set; } = [];
    public string? ShutdownDumpDirectory { get; set; }
    public EmberTraceRequestOptions Requests { get; set; } = new();
    public EmberTraceDumpOptions Dump { get; set; } = new();
}
