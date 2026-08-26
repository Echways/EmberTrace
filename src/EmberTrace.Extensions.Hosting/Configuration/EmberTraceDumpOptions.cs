namespace EmberTrace.Extensions.Hosting.Configuration;

public sealed class EmberTraceDumpOptions
{
    public const string ApiKeyHeader = "X-EmberTrace-Key";

    public bool Enabled { get; set; }
    public string Path { get; set; } = "/embertrace/dump";
    public string? ApiKey { get; set; }
    public string? AuthorizationPolicy { get; set; }
    public bool RestrictToLoopback { get; set; } = true;
    public bool AllowAnonymous { get; set; }
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan MaxWindow { get; set; } = TimeSpan.FromMinutes(5);
    public string FileNamePrefix { get; set; } = "embertrace";
}
