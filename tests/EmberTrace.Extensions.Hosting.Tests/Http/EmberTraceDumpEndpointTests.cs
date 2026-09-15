using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Extensions.Hosting.Http;
using EmberTrace.Extensions.Hosting.Recording;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EmberTrace.Extensions.Hosting.Tests.Http;

[TestClass]
[DoNotParallelize]
public sealed class EmberTraceDumpEndpointTests
{
    private const string Key = "0123456789abcdef01";

    [TestCleanup]
    public void Cleanup()
    {
        if (Tracer.IsRunning)
            Tracer.Stop();
    }

    [TestMethod]
    [DataRow(false, true, null, null, null, null, StatusCodes.Status404NotFound)]
    [DataRow(true, true, null, null, null, null, StatusCodes.Status200OK)]
    [DataRow(true, true, "127.0.0.1", null, null, null, StatusCodes.Status200OK)]
    [DataRow(true, true, "::1", null, null, null, StatusCodes.Status200OK)]
    [DataRow(true, true, "::ffff:127.0.0.1", null, null, null, StatusCodes.Status200OK)]
    [DataRow(true, true, "203.0.113.5", null, null, null, StatusCodes.Status404NotFound)]
    [DataRow(true, true, "127.0.0.1", "X-Forwarded-For", null, null, StatusCodes.Status404NotFound)]
    [DataRow(true, true, "127.0.0.1", "Forwarded", null, null, StatusCodes.Status404NotFound)]
    [DataRow(true, false, "203.0.113.5", "X-Forwarded-For", Key, Key, StatusCodes.Status200OK)]
    [DataRow(true, true, "127.0.0.1", null, Key, null, StatusCodes.Status401Unauthorized)]
    [DataRow(true, true, "127.0.0.1", null, Key, "0123456789abcdef02", StatusCodes.Status401Unauthorized)]
    [DataRow(true, true, "127.0.0.1", null, Key, Key + "0", StatusCodes.Status401Unauthorized)]
    [DataRow(true, true, "127.0.0.1", null, Key, Key, StatusCodes.Status200OK)]
    [DataRow(true, true, "203.0.113.5", null, Key, Key, StatusCodes.Status404NotFound)]
    public async Task Access_IsGuardedByEnablementLoopbackAndApiKey(
        bool enabled, bool loopbackOnly, string? remoteIp, string? forwardedHeader, string? configuredKey,
        string? providedKey, int expectedStatus)
    {
        using var provider = Build(options =>
        {
            options.Dump.Enabled = enabled;
            options.Dump.RestrictToLoopback = loopbackOnly;
            options.Dump.ApiKey = configuredKey;
        });
        StartWithEvents(provider);

        var context = Request(provider);
        if (remoteIp is not null)
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        if (forwardedHeader is not null)
            context.Request.Headers[forwardedHeader] = "203.0.113.5";
        if (providedKey is not null)
            context.Request.Headers[EmberTraceDumpOptions.ApiKeyHeader] = providedKey;

        await EmberTraceDumpEndpoint.HandleAsync(context);

        Assert.AreEqual(expectedStatus, context.Response.StatusCode);
        Assert.AreEqual(expectedStatus == StatusCodes.Status200OK, BodyOf(context).Length > 0);
        Assert.IsTrue(Tracer.IsRunning);
    }

    [TestMethod]
    public async Task DefaultFormat_ReturnsAReadableEmberSnapshotWithHeaders()
    {
        using var provider = Build(options => options.Dump.FileNamePrefix = "svc");
        StartWithEvents(provider);
        var context = Request(provider);

        await EmberTraceDumpEndpoint.HandleAsync(context);

        var body = BodyOf(context);
        var session = TraceFormat.Read(new MemoryStream(body));

        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.AreEqual("application/octet-stream", context.Response.ContentType);
        Assert.AreEqual(body.Length, context.Response.ContentLength);
        StringAssert.Matches(context.Response.Headers.ContentDisposition.ToString(),
            new Regex("^attachment; filename=\"svc-\\d{8}-\\d{6}-\\d{3}\\.ember\"$"));
        Assert.AreEqual("1", context.Response.Headers["X-EmberTrace-Events"].ToString());
        Assert.AreEqual("0", context.Response.Headers["X-EmberTrace-Dropped"].ToString());
        Assert.IsTrue(session.IsSnapshot);
        Assert.AreEqual(Tracer.Id("dump-probe"), session.SortedEvents().Single().Id);
    }

    [TestMethod]
    [DataRow("?format=chrome")]
    [DataRow("?format=CHROME")]
    public async Task ChromeFormat_ReturnsTraceEventJson(string query)
    {
        using var provider = Build(_ => { });
        StartWithEvents(provider);
        var context = Request(provider, query);

        await EmberTraceDumpEndpoint.HandleAsync(context);

        Assert.AreEqual("application/json", context.Response.ContentType);
        StringAssert.EndsWith(context.Response.Headers.ContentDisposition.ToString(), ".json\"");

        using var document = JsonDocument.Parse(BodyOf(context));
        Assert.IsTrue(document.RootElement.GetProperty("traceEvents").EnumerateArray()
            .Any(e => e.GetProperty("name").GetString() == "dump-probe"));
    }

    [TestMethod]
    public async Task CollapsedFormat_ReturnsFlameGraphText()
    {
        using var provider = Build(_ => { });
        provider.GetRequiredService<EmberTraceRecorder>().TryStart();
        using (Tracer.Scope(Tracer.Id("dump-scope")))
            Thread.Sleep(2);

        var context = Request(provider, "?format=collapsed");

        await EmberTraceDumpEndpoint.HandleAsync(context);

        Assert.AreEqual("text/plain; charset=utf-8", context.Response.ContentType);
        StringAssert.EndsWith(context.Response.Headers.ContentDisposition.ToString(), ".folded\"");
        StringAssert.Matches(Encoding.UTF8.GetString(BodyOf(context)),
            new Regex("^dump-scope \\d+\n$"));
    }

    [TestMethod]
    public async Task UnknownFormat_Returns400()
    {
        using var provider = Build(_ => { });
        StartWithEvents(provider);
        var context = Request(provider, "?format=parquet");

        await EmberTraceDumpEndpoint.HandleAsync(context);

        Assert.AreEqual(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [TestMethod]
    public async Task StoppedSession_Returns503()
    {
        using var provider = Build(_ => { });
        var context = Request(provider);

        await EmberTraceDumpEndpoint.HandleAsync(context);

        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
    }

    [TestMethod]
    [DataRow("", 10.0)]
    [DataRow("?window=5", 5.0)]
    [DataRow("?window=2.5", 2.5)]
    [DataRow("?window=00:00:03", 3.0)]
    [DataRow("?window=900", 300.0)]
    [DataRow("?window=00:30:00", 300.0)]
    [DataRow("?window=0", 0.0)]
    [DataRow("?window=-5", 0.0)]
    [DataRow("?window=-00:00:05", 0.0)]
    [DataRow("?window=NaN", 10.0)]
    [DataRow("?window=abc", 10.0)]
    [DataRow("?window=1e300", 300.0)]
    [DataRow("?window=Infinity", 300.0)]
    [DataRow("?window=-Infinity", 0.0)]
    public void ResolveWindow_ParsesSecondsOrTimeSpansAndClampsToTheMaximum(string query, double expectedSeconds)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(query);

        var window = EmberTraceDumpEndpoint.ResolveWindow(context.Request, new EmberTraceDumpOptions
        {
            Window = TimeSpan.FromSeconds(10),
            MaxWindow = TimeSpan.FromMinutes(5)
        });

        Assert.AreEqual(TimeSpan.FromSeconds(expectedSeconds), window);
    }

    private static ServiceProvider Build(Action<EmberTraceOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddEmberTrace(options =>
        {
            options.Dump.Enabled = true;
            configure(options);
        });
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext Request(ServiceProvider provider, string query = "")
    {
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Method = "GET";
        context.Request.Path = "/embertrace/dump";
        context.Request.QueryString = new QueryString(query);
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static byte[] BodyOf(HttpContext context)
    {
        return ((MemoryStream)context.Response.Body).ToArray();
    }

    private static void StartWithEvents(ServiceProvider provider)
    {
        provider.GetRequiredService<EmberTraceRecorder>().TryStart();
        Tracer.Instant(Tracer.Id("dump-probe"));
    }
}
