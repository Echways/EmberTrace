using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Sessions;

namespace EmberTrace.Extensions.Hosting.Tests.Configuration;

[TestClass]
public sealed class EmberTraceOptionsValidatorTests
{
    private static readonly EmberTraceOptionsValidator Validator = new();

    public static IEnumerable<object[]> ValidOptions =>
    [
        [new EmberTraceOptions()],
        [new EmberTraceOptions { OverflowPolicy = OverflowPolicy.DropNew, MaxRetentionWindow = TimeSpan.Zero }],
        [new EmberTraceOptions { Dump = { Enabled = true } }],
        [Unguarded(dump => dump.ApiKey = new string('k', 16))],
        [Unguarded(dump => dump.AuthorizationPolicy = "ops")],
        [Unguarded(dump => dump.AllowAnonymous = true)],
        [new EmberTraceOptions { Dump = { Path = "relative", ApiKey = "short", MaxWindow = TimeSpan.Zero } }],
        [SlowRequests(slow => slow.Directory = "/var/tmp/embertrace")],
        [SlowRequests(slow => slow.Window = TimeSpan.Zero)],
        [new EmberTraceOptions { SlowRequests = { Threshold = TimeSpan.Zero } }]
    ];

    public static IEnumerable<object[]> InvalidOptions =>
    [
        [new EmberTraceOptions { ChunkCapacity = 0 }, "EmberTrace:ChunkCapacity"],
        [new EmberTraceOptions { MaxRetentionWindow = TimeSpan.FromSeconds(-1) }, "MaxRetentionWindow cannot be negative"],
        [new EmberTraceOptions { OverflowPolicy = OverflowPolicy.DropNew }, "but it is DropNew"],
        [new EmberTraceOptions { RuntimeCounterInterval = TimeSpan.Zero }, "EmberTrace:RuntimeCounterInterval"],
        [new EmberTraceOptions { Requests = { MaxTrackedRoutes = 0 } }, "EmberTrace:Requests:MaxTrackedRoutes"],
        [Dump(dump => dump.Path = "embertrace/dump"), "EmberTrace:Dump:Path"],
        [Dump(dump => dump.ApiKey = "short"), "at least 16 characters"],
        [Dump(dump => dump.Window = TimeSpan.FromSeconds(-1)), "Dump:Window cannot be negative"],
        [Dump(dump => (dump.Window, dump.MaxWindow) = (TimeSpan.Zero, TimeSpan.Zero)), "MaxWindow must be greater"],
        [Dump(dump => dump.Window = TimeSpan.FromMinutes(10)), "Window cannot exceed"],
        [Unguarded(_ => { }), "without a guard"],
        [SlowRequests(slow => slow.Directory = null), "SlowRequests:Directory"],
        [SlowRequests(slow => slow.Threshold = TimeSpan.Zero), "SlowRequests:Threshold"],
        [SlowRequests(slow => slow.Cooldown = TimeSpan.FromSeconds(-1)), "SlowRequests:Cooldown"],
        [SlowRequests(slow => slow.Window = TimeSpan.FromMilliseconds(999)), "SlowRequests:Window"],
        [new EmberTraceOptions { SlowRequests = { Enabled = true, Directory = "/tmp" }, Requests = { Enabled = false } },
            "requires EmberTrace:Requests:Enabled"]
    ];

    [TestMethod]
    [DynamicData(nameof(ValidOptions))]
    public void ValidOptions_Succeed(EmberTraceOptions options)
    {
        var result = Validator.Validate(null, options);

        Assert.IsTrue(result.Succeeded, result.FailureMessage);
    }

    [TestMethod]
    [DynamicData(nameof(InvalidOptions))]
    public void InvalidOptions_FailWithExactlyTheOffendingRule(EmberTraceOptions options, string expected)
    {
        var result = Validator.Validate(null, options);

        Assert.IsTrue(result.Failed);
        Assert.HasCount(1, result.Failures!);
        Assert.Contains(expected, result.Failures!.Single());
    }

    [TestMethod]
    public void SeveralViolations_AreAllReported()
    {
        var options = new EmberTraceOptions
        {
            ChunkCapacity = 0,
            RuntimeCounterInterval = TimeSpan.Zero,
            Dump = { Enabled = true, Path = "dump" }
        };

        Assert.HasCount(3, Validator.Validate(null, options).Failures!);
    }

    private static EmberTraceOptions Dump(Action<EmberTraceDumpOptions> configure)
    {
        var options = new EmberTraceOptions { Dump = { Enabled = true } };
        configure(options.Dump);
        return options;
    }

    private static EmberTraceOptions Unguarded(Action<EmberTraceDumpOptions> configure)
    {
        return Dump(dump =>
        {
            dump.RestrictToLoopback = false;
            configure(dump);
        });
    }

    private static EmberTraceOptions SlowRequests(Action<EmberTraceSlowRequestOptions> configure)
    {
        var options = new EmberTraceOptions { SlowRequests = { Enabled = true, Directory = "/tmp" } };
        configure(options.SlowRequests);
        return options;
    }
}
