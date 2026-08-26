using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Extensions.Hosting.Recording;
using EmberTrace.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EmberTrace.Extensions.Hosting.Tests.DependencyInjection;

[TestClass]
[DoNotParallelize]
public sealed class AddEmberTraceTests
{
    [TestCleanup]
    public void Cleanup()
    {
        if (Tracer.IsRunning)
            Tracer.Stop();
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] entries)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
            map[entry.Key] = entry.Value;

        return new ConfigurationBuilder().AddInMemoryCollection(map).Build();
    }

    private static ServiceProvider Build(IConfiguration configuration, Action<EmberTraceOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddEmberTrace(configure);
        return services.BuildServiceProvider();
    }

    [TestMethod]
    public void Options_AreBoundFromConfiguration()
    {
        using var provider = Build(Configuration(
            ("EmberTrace:ChunkCapacity", "2048"),
            ("EmberTrace:MaxRetentionWindow", "00:00:05"),
            ("EmberTrace:RuntimeCounters", "Gc, Memory"),
            ("EmberTrace:Requests:Category", "Web"),
            ("EmberTrace:Requests:IgnoredPaths:0", "/metrics"),
            ("EmberTrace:Dump:Enabled", "true"),
            ("EmberTrace:Dump:Window", "00:00:07")));

        var options = provider.GetRequiredService<IOptions<EmberTraceOptions>>().Value;

        Assert.AreEqual(2048, options.ChunkCapacity);
        Assert.AreEqual(TimeSpan.FromSeconds(5), options.MaxRetentionWindow);
        Assert.AreEqual(RuntimeCounters.Gc | RuntimeCounters.Memory, options.RuntimeCounters);
        Assert.AreEqual("Web", options.Requests.Category);
        CollectionAssert.AreEqual(new[] { "/metrics" }, options.Requests.IgnoredPaths);
        Assert.IsTrue(options.Dump.Enabled);
        Assert.AreEqual(TimeSpan.FromSeconds(7), options.Dump.Window);
    }

    [TestMethod]
    public void ConfigureDelegate_WinsOverConfiguration()
    {
        using var provider = Build(
            Configuration(("EmberTrace:ChunkCapacity", "2048")),
            options => options.ChunkCapacity = 4096);

        Assert.AreEqual(4096, provider.GetRequiredService<IOptions<EmberTraceOptions>>().Value.ChunkCapacity);
    }

    [TestMethod]
    public void InvalidOptions_ThrowOnResolve()
    {
        using var provider = Build(Configuration(
            ("EmberTrace:OverflowPolicy", "DropNew"),
            ("EmberTrace:MaxRetentionWindow", "00:00:05")));

        try
        {
            _ = provider.GetRequiredService<IOptions<EmberTraceOptions>>().Value;
            Assert.Fail("Expected OptionsValidationException.");
        }
        catch (OptionsValidationException ex)
        {
            StringAssert.Contains(string.Join(" ", ex.Failures), "MaxRetentionWindow");
        }
    }

    [TestMethod]
    public void RecorderAndHostedService_AreRegistered()
    {
        using var provider = Build(Configuration());

        Assert.IsNotNull(provider.GetService<EmberTraceRecorder>());

        var hosted = provider.GetServices<IHostedService>().ToArray();
        Assert.AreEqual(1, hosted.Count(service => service.GetType().Name == "EmberTraceHostedService"));
    }

    [TestMethod]
    public void Recorder_IsASingleton()
    {
        using var provider = Build(Configuration());

        Assert.AreSame(
            provider.GetRequiredService<EmberTraceRecorder>(),
            provider.GetRequiredService<EmberTraceRecorder>());
    }

    [TestMethod]
    public void AddEmberTrace_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Configuration());
        services.AddEmberTrace();
        services.AddEmberTrace();

        using var provider = services.BuildServiceProvider();

        var hosted = provider.GetServices<IHostedService>().ToArray();
        Assert.AreEqual(1, hosted.Count(service => service.GetType().Name == "EmberTraceHostedService"));
    }

    [TestMethod]
    public void CustomSection_IsBound()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Configuration(("Tracing:ChunkCapacity", "8192")));
        services.AddEmberTrace("Tracing");

        using var provider = services.BuildServiceProvider();

        Assert.AreEqual(8192, provider.GetRequiredService<IOptions<EmberTraceOptions>>().Value.ChunkCapacity);
    }

    [TestMethod]
    public void ConfigurationOverload_BindsTheSection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEmberTrace(Configuration(("EmberTrace:ChunkCapacity", "1024")));

        using var provider = services.BuildServiceProvider();

        Assert.AreEqual(1024, provider.GetRequiredService<IOptions<EmberTraceOptions>>().Value.ChunkCapacity);
    }
}
