using EmberTrace.Sessions;
using Microsoft.Extensions.Options;

namespace EmberTrace.Extensions.Hosting.Configuration;

internal sealed class EmberTraceOptionsValidator : IValidateOptions<EmberTraceOptions>
{
    private const int MinimumApiKeyLength = 16;

    public ValidateOptionsResult Validate(string? name, EmberTraceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.ChunkCapacity <= 0)
            failures.Add("EmberTrace:ChunkCapacity must be greater than zero.");

        if (options.MaxRetentionWindow < TimeSpan.Zero)
            failures.Add("EmberTrace:MaxRetentionWindow cannot be negative.");

        if (options.MaxRetentionWindow > TimeSpan.Zero && options.OverflowPolicy != OverflowPolicy.DropOldest)
            failures.Add(
                "EmberTrace:MaxRetentionWindow requires EmberTrace:OverflowPolicy to be DropOldest, " +
                $"but it is {options.OverflowPolicy}.");

        if (options.RuntimeCounterInterval <= TimeSpan.Zero)
            failures.Add("EmberTrace:RuntimeCounterInterval must be greater than zero.");

        if (options.Requests.MaxTrackedRoutes <= 0)
            failures.Add("EmberTrace:Requests:MaxTrackedRoutes must be greater than zero.");

        ValidateDump(options.Dump, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateDump(EmberTraceDumpOptions dump, List<string> failures)
    {
        if (!dump.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(dump.Path) || dump.Path[0] != '/')
            failures.Add("EmberTrace:Dump:Path must start with '/'.");

        if (dump.ApiKey is not null && dump.ApiKey.Length < MinimumApiKeyLength)
            failures.Add($"EmberTrace:Dump:ApiKey must be at least {MinimumApiKeyLength} characters long.");

        if (dump.Window < TimeSpan.Zero)
            failures.Add("EmberTrace:Dump:Window cannot be negative.");

        if (dump.MaxWindow <= TimeSpan.Zero)
            failures.Add("EmberTrace:Dump:MaxWindow must be greater than zero.");

        if (dump.Window > dump.MaxWindow)
            failures.Add("EmberTrace:Dump:Window cannot exceed EmberTrace:Dump:MaxWindow.");

        var guarded = dump.RestrictToLoopback
                      || dump.AllowAnonymous
                      || !string.IsNullOrWhiteSpace(dump.ApiKey)
                      || !string.IsNullOrWhiteSpace(dump.AuthorizationPolicy);

        if (!guarded)
            failures.Add(
                "EmberTrace:Dump is enabled without a guard. Set one of ApiKey, AuthorizationPolicy, " +
                "RestrictToLoopback, or AllowAnonymous.");
    }
}
