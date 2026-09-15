namespace EmberTrace.Extensions.Hosting.Configuration;

public sealed class EmberTraceSlowRequestOptions
{
    public bool Enabled { get; set; }
    public TimeSpan Threshold { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromMinutes(1);
    public string? Directory { get; set; }
}
