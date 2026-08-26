namespace EmberTrace.Extensions.Hosting.Configuration;

public sealed class EmberTraceRequestOptions
{
    public static readonly string[] DefaultIgnoredPaths = ["/health", "/healthz", "/embertrace"];

    public bool Enabled { get; set; } = true;
    public bool UseRoutePattern { get; set; } = true;
    public bool RecordFlow { get; set; } = true;
    public string Category { get; set; } = "Http";
    public int MaxTrackedRoutes { get; set; } = 1024;
    public string[] IgnoredPaths { get; set; } = DefaultIgnoredPaths;
}
