using System.Net;
using System.Text.Json;
using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Extensions.Hosting.Http;
using EmberTrace.Extensions.Hosting.Recording;
using EmberTrace.Sessions;
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

    private static ServiceProvider Build(Action<EmberTraceOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddEmberTrace(configure);
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
        var body = (MemoryStream)context.Response.Body;
        return body.ToArray();
    }

    private static void StartWithEvents(ServiceProvider provider)
    {
        provider.GetRequiredService<EmberTraceRecorder>().TryStart();
        Tracer.Instant(Tracer.Id("dump-probe"));
    }

    [TestMethod]
    public async Task DisabledDump_Returns404()
    {
        using var provider = Build(static options => options.Dump.Enabled = false);
        StartWithEvents(provider);
        var context = Request(provider);

        await EmberTraceDumpEndpoint.HandleAsync(context);

        Assert.AreEqual(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [TestMethod]
    public async Task NonLoopbackCaller_Returns404()
    {
        using var provider = Build(static options => options.Dump.Enabled = true);
        StartWithEvents(provider);
        var context = Request(provider);
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.5");

        await EmberTraceDumpEndpoint.HandleAsync(context);

        Assert.AreEqual(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [TestMethod]
    public async Task LoopbackCaller_IsAllowed()
    {
        using var provider = Build(static options => options.Dump.Enabled = true);
        StartWithEvents(provider);
        var context = Request(provider);
        context.Connection.RemoteIpAddress = IPAddress.Loopback;

        await EmberTraceDumpEndpoint.HandleAsync(context);

        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [TestMethod]
    public async Task MissingApiKey_Returns401()
    {
        using var provider = Build(static options =>
        {
            options.Dump.Enabled = true;
            options.Dump.ApiKey = Key;
        });
        StartWithEvents(provider);
        var context = Request(provider);

        await EmberTraceDumpEndpoint.HandleAsync(context);

        Assert.AreEqual(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.AreEqual(0, BodyOf(context).Length);
    }

    [TestMethod]
    public async Task WrongApiKey_Returns401()
    {
        using var provider = Build(static options =>
        {
            options.Dump.Enabled = true;
            options.Dump.ApiKey = Key;
        });
        StartWithEvents(provider);
        var context = Request(provider);
        context.Request.Headers[EmberTraceDumpOptions.ApiKeyHeader] = "0123456789abcdef02";

        await EmberTraceDumpEndpoint.HandleAsync(context);

        Assert.AreEqual(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [TestMethod]
    public async Task CorrectApiKey_ReturnsAReadableSession()
    {
        using var provider = Build(static options =>
        {
            options.Dump.Enabled = true;
            options.Dump.ApiKey = Key;
        });
        StartWithEvents(provider);
        var context = Request(provider);
        context.Request.Headers[EmberTraceDumpOptions.ApiKeyHeader] = Key;

        await EmberTraceDumpEndpoint.HandleAsync(context);

        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.AreEqual("application/octet-stream", context.Response.ContentType);
        StringAssert.Contains(context.Response.Headers.ContentDisposition.ToString(), ".ember");

        var session = TraceFormat.Read(new MemoryStream(BodyOf(context)));
        Assert.IsTrue(session.IsSnapshot);
        Assert.AreEqual(1L, session.EventCount);
    }

    [TestMethod]
    public async Task SessionKeepsRunningAfterADump()
    {
        using var provider = Build(static options => options.Dump.Enabled = true);
        StartWithEvents(provider);

        await EmberTraceDumpEndpoint.HandleAsync(Request(provider));

        Assert.IsTrue(Tracer.IsRunning);
    }

    [TestMethod]
    public async Task ChromeFormat_ReturnsJson()
    {
        using var provider = Build(static options => options.Dump.Enabled = true);
        StartWithEvents(provider);
        var context = Request(provider, "?format=chrome");

        await EmberTraceDumpEndpoint.HandleAsync(context);

        Assert.AreEqual("application/json", context.Response.ContentType);
        StringAssert.Contains(context.Response.Headers.ContentDisposition.ToString(), ".json");

        using var document = JsonDocument.Parse(BodyOf(context));
        Assert.IsTrue(document.RootElement.TryGetProperty("traceEvents", out _));
    }

    [TestMethod]
    public async Task UnknownFormat_Returns400()
    {
        using var provider = Build(static options => options.Dump.Enabled = true);
        StartWithEvents(provider);
        var context = Request(provider, "?format=parquet");

        await EmberTraceDumpEndpoint.HandleAsync(context);

        Assert.AreEqual(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [TestMethod]
    public async Task StoppedSession_Returns503()
    {
        using var provider = Build(static options => options.Dump.Enabled = true);
        var context = Request(provider);

        await EmberTraceDumpEndpoint.HandleAsync(context);

        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
    }

    [TestMethod]
    public async Task EventCountHeader_IsWritten()
    {
        using var provider = Build(static options => options.Dump.Enabled = true);
        StartWithEvents(provider);
        var context = Request(provider);

        await EmberTraceDumpEndpoint.HandleAsync(context);

        Assert.AreEqual("1", context.Response.Headers["X-EmberTrace-Events"].ToString());
        Assert.AreEqual("0", context.Response.Headers["X-EmberTrace-Dropped"].ToString());
        Assert.AreEqual(BodyOf(context).Length, context.Response.ContentLength);
    }

    [TestMethod]
    [DataRow("?window=5", 5)]
    [DataRow("?window=00:00:03", 3)]
    [DataRow("?window=900", 300)]
    public async Task WindowQuery_IsParsedAndClamped(string query, int expectedSeconds)
    {
        using var provider = Build(static options =>
        {
            options.Dump.Enabled = true;
            options.Dump.MaxWindow = TimeSpan.FromMinutes(5);
        });
        StartWithEvents(provider);
        var context = Request(provider, query);

        await EmberTraceDumpEndpoint.HandleAsync(context);

        Assert.AreEqual(
            TimeSpan.FromSeconds(expectedSeconds),
            EmberTraceDumpEndpoint.ResolveWindow(context.Request, provider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<EmberTraceOptions>>().Value.Dump));
    }
}
