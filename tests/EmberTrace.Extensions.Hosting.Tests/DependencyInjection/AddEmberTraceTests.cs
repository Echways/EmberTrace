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
    [TestMethod]
    public void Options_AreBoundFromConfiguration()
    {
        using var provider = Build(services => services.AddEmberTrace(), Configuration(
            ("EmberTrace:ChunkCapacity", "2048"),
            ("EmberTrace:MaxRetentionWindow", "00:00:05"),
            ("EmberTrace:RuntimeCounters", "Gc, Memory"),
            ("EmberTrace:EnabledCategories:0", "Db"),
            ("EmberTrace:Requests:Category", "Web"),
            ("EmberTrace:Requests:IgnoredPaths:0", "/metrics"),
            ("EmberTrace:Dump:Enabled", "true"),
            ("EmberTrace:Dump:Window", "00:00:07")));

        var options = OptionsOf(provider);

        Assert.AreEqual(2048, options.ChunkCapacity);
        Assert.AreEqual(TimeSpan.FromSeconds(5), options.MaxRetentionWindow);
        Assert.AreEqual(RuntimeCounters.Gc | RuntimeCounters.Memory, options.RuntimeCounters);
        CollectionAssert.AreEqual(new[] { "Db" }, options.EnabledCategories);
        Assert.AreEqual("Web", options.Requests.Category);
        CollectionAssert.AreEqual(new[] { "/metrics" }, options.Requests.IgnoredPaths);
        Assert.IsTrue(options.Dump.Enabled);
        Assert.AreEqual(TimeSpan.FromSeconds(7), options.Dump.Window);
    }

    [TestMethod]
    public void IgnoredPaths_FallBackToTheDefaultsWhenNotConfigured()
    {
        using var provider = Build(services => services.AddEmberTrace(), Configuration());

        CollectionAssert.AreEqual(EmberTraceRequestOptions.DefaultIgnoredPaths, OptionsOf(provider).Requests.IgnoredPaths);
    }

    [TestMethod]
    public void ConfigureDelegate_WinsOverConfiguration()
    {
        using var provider = Build(
            services => services.AddEmberTrace(options => options.ChunkCapacity = 4096),
            Configuration(("EmberTrace:ChunkCapacity", "2048")));

        Assert.AreEqual(4096, OptionsOf(provider).ChunkCapacity);
    }

    [TestMethod]
    public void CustomSection_IsBound()
    {
        using var provider = Build(services => services.AddEmberTrace("Tracing"),
            Configuration(("Tracing:ChunkCapacity", "8192"), ("EmberTrace:ChunkCapacity", "2048")));

        Assert.AreEqual(8192, OptionsOf(provider).ChunkCapacity);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ConfigurationOverload_BindsTheEmberTraceSectionOrAGivenSection(bool passSection)
    {
        var configuration = Configuration(("EmberTrace:ChunkCapacity", "1024"));
        var services = new ServiceCollection().AddLogging();
        services.AddEmberTrace(passSection ? configuration.GetSection("EmberTrace") : configuration);

        using var provider = services.BuildServiceProvider();

        Assert.AreEqual(1024, OptionsOf(provider).ChunkCapacity);
    }

    [TestMethod]
    public void InvalidOptions_ThrowOnResolve()
    {
        using var provider = Build(services => services.AddEmberTrace(), Configuration(
            ("EmberTrace:OverflowPolicy", "DropNew"),
            ("EmberTrace:MaxRetentionWindow", "00:00:05")));

        var ex = Assert.ThrowsExactly<OptionsValidationException>(() => OptionsOf(provider));

        Assert.Contains("MaxRetentionWindow", string.Join(" ", ex.Failures));
    }

    [TestMethod]
    public void AddEmberTrace_RegistersSingletonsOnceEvenWhenCalledTwice()
    {
        using var provider = Build(services => services.AddEmberTrace().AddEmberTrace(), Configuration());

        Assert.AreSame(provider.GetRequiredService<EmberTraceRecorder>(), provider.GetRequiredService<EmberTraceRecorder>());
        Assert.AreSame(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
        Assert.AreEqual(1, provider.GetServices<IHostedService>().Count(s => s is EmberTraceHostedService));
        Assert.AreEqual(1, provider.GetServices<IValidateOptions<EmberTraceOptions>>().Count());
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  ")]
    public void AddEmberTrace_WithABlankSectionName_Throws(string? sectionName)
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddEmberTrace(sectionName!));
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] entries)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();
    }

    private static ServiceProvider Build(Action<IServiceCollection> register, IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        register(services);
        return services.BuildServiceProvider();
    }

    private static EmberTraceOptions OptionsOf(IServiceProvider provider)
    {
        return provider.GetRequiredService<IOptions<EmberTraceOptions>>().Value;
    }
}
