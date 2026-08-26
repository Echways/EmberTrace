using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Sessions;

namespace EmberTrace.Extensions.Hosting.Tests.Configuration;

[TestClass]
[DoNotParallelize]
public sealed class EmberTraceOptionsValidatorTests
{
    private static readonly EmberTraceOptionsValidator Validator = new();

    [TestMethod]
    public void Defaults_AreValid()
    {
        Assert.IsTrue(Validator.Validate(null, new EmberTraceOptions()).Succeeded);
    }

    [TestMethod]
    public void RetentionWindow_RequiresDropOldest()
    {
        var options = new EmberTraceOptions { OverflowPolicy = OverflowPolicy.DropNew };

        var result = Validator.Validate(null, options);

        Assert.IsTrue(result.Failed);
        StringAssert.Contains(result.FailureMessage, "MaxRetentionWindow");
    }

    [TestMethod]
    public void RetentionWindow_MayBeZeroWithDropNew()
    {
        var options = new EmberTraceOptions
        {
            OverflowPolicy = OverflowPolicy.DropNew,
            MaxRetentionWindow = TimeSpan.Zero
        };

        Assert.IsTrue(Validator.Validate(null, options).Succeeded);
    }

    [TestMethod]
    public void EnabledDump_WithoutAnyGuard_IsRejected()
    {
        var options = new EmberTraceOptions
        {
            Dump = new EmberTraceDumpOptions { Enabled = true, RestrictToLoopback = false }
        };

        var result = Validator.Validate(null, options);

        Assert.IsTrue(result.Failed);
        StringAssert.Contains(result.FailureMessage, "ApiKey");
    }

    [TestMethod]
    public void EnabledDump_WithLoopbackOnly_IsAccepted()
    {
        var options = new EmberTraceOptions { Dump = new EmberTraceDumpOptions { Enabled = true } };

        Assert.IsTrue(Validator.Validate(null, options).Succeeded);
    }

    [TestMethod]
    public void EnabledDump_WithExplicitAnonymous_IsAccepted()
    {
        var options = new EmberTraceOptions
        {
            Dump = new EmberTraceDumpOptions
            {
                Enabled = true,
                RestrictToLoopback = false,
                AllowAnonymous = true
            }
        };

        Assert.IsTrue(Validator.Validate(null, options).Succeeded);
    }

    [TestMethod]
    public void ShortApiKey_IsRejected()
    {
        var options = new EmberTraceOptions
        {
            Dump = new EmberTraceDumpOptions { Enabled = true, ApiKey = "short" }
        };

        var result = Validator.Validate(null, options);

        Assert.IsTrue(result.Failed);
        StringAssert.Contains(result.FailureMessage, "16");
    }

    [TestMethod]
    public void DumpPath_MustBeRooted()
    {
        var options = new EmberTraceOptions
        {
            Dump = new EmberTraceDumpOptions { Enabled = true, Path = "embertrace/dump" }
        };

        var result = Validator.Validate(null, options);

        Assert.IsTrue(result.Failed);
        StringAssert.Contains(result.FailureMessage, "Path");
    }

    [TestMethod]
    public void DumpWindow_MustNotExceedMaxWindow()
    {
        var options = new EmberTraceOptions
        {
            Dump = new EmberTraceDumpOptions
            {
                Enabled = true,
                Window = TimeSpan.FromMinutes(10),
                MaxWindow = TimeSpan.FromMinutes(5)
            }
        };

        var result = Validator.Validate(null, options);

        Assert.IsTrue(result.Failed);
        StringAssert.Contains(result.FailureMessage, "MaxWindow");
    }

    [TestMethod]
    public void NonPositiveChunkCapacity_IsRejected()
    {
        var result = Validator.Validate(null, new EmberTraceOptions { ChunkCapacity = 0 });

        Assert.IsTrue(result.Failed);
        StringAssert.Contains(result.FailureMessage, "ChunkCapacity");
    }
}
