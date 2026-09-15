using System.Diagnostics;
using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Extensions.Hosting.Http;
using EmberTrace.Extensions.Hosting.Recording;
using EmberTrace.Sessions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Bridge = EmberTrace.ActivityBridge.ActivityBridge;

namespace EmberTrace.Extensions.Hosting.Tests.Http;

[TestClass]
[DoNotParallelize]
public sealed class EmberTraceMiddlewareTests
{
    [TestInitialize]
    public void Setup()
    {
        Activity.Current = null;
        HttpTraceIds.Clear();
        HttpTraceIds.EnsureRegistered();
        Tracer.Start(SessionOptionsFactory.Create(new EmberTraceOptions()));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Tracer.IsRunning)
            Tracer.Stop();

        HttpTraceIds.Clear();
    }

    [TestMethod]
    public async Task Request_IsWrappedInAScopeAndAFlowPublishedOnTheContext()
    {
        long observed = 0;
        var middleware = Create(context =>
        {
            observed = context.GetEmberTraceFlowId();
            return Task.CompletedTask;
        });

        var context = Request("GET", "/orders/17", "/orders/{id}");
        await middleware.InvokeAsync(context);

        var id = Tracer.Id("GET /orders/{id}");
        var events = StopAndCollect();

        CollectionAssert.AreEqual(
            new[] { TraceEventKind.FlowStart, TraceEventKind.Begin, TraceEventKind.FlowEnd, TraceEventKind.End },
            events.Select(e => e.Kind).ToArray());
        Assert.IsTrue(events.All(e => e.Id == id));
        Assert.AreNotEqual(0L, observed);
        Assert.AreEqual(observed, events[0].FlowId);
        Assert.AreEqual(observed, context.GetEmberTraceFlowId());
    }

    [TestMethod]
    public async Task FlowId_FollowsTheCurrentW3CActivity()
    {
        using var activity = new Activity("request");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();

        var context = Request("GET", "/orders/17", "/orders/{id}");
        await Create(static _ => Task.CompletedTask).InvokeAsync(context);

        Assert.AreEqual(Bridge.FlowIdFromTraceId(activity.TraceId), context.GetEmberTraceFlowId());
    }

    [TestMethod]
    [DataRow("POST", "/anything", null, true, "HTTP POST")]
    [DataRow("GET", "/orders/17", "/orders/{id}", true, "GET /orders/{id}")]
    [DataRow("GET", "/orders/17", "/orders/{id}", false, "HTTP GET")]
    public async Task ScopeName_UsesTheRoutePatternWhenAvailableAndEnabled(
        string method, string path, string? pattern, bool useRoutePattern, string expected)
    {
        var options = new EmberTraceOptions { Requests = { UseRoutePattern = useRoutePattern, Category = "Web" } };

        await Create(static _ => Task.CompletedTask, options).InvokeAsync(Request(method, path, pattern));

        var session = Tracer.Stop();
        var id = session.SortedEvents().First(e => e.Kind == TraceEventKind.Begin).Id;

        Assert.AreEqual(Tracer.Id(expected), id);
        Assert.IsTrue(session.Metadata.TryGet(id, out var meta));
        Assert.AreEqual("Web", meta.Category);
    }

    [TestMethod]
    [DataRow("/health", false)]
    [DataRow("/HEALTH/ready", false)]
    [DataRow("/embertrace/dump", false)]
    [DataRow("/healthcheck", true)]
    [DataRow("/orders/health", true)]
    public async Task IgnoredPaths_MatchWholeLeadingSegmentsCaseInsensitively(string path, bool traced)
    {
        await Create(static _ => Task.CompletedTask).InvokeAsync(Request("GET", path));

        Assert.AreEqual(traced, StopAndCollect().Count > 0);
    }

    [TestMethod]
    public async Task IgnoredPaths_WithoutALeadingSlash_AreSkipped()
    {
        var options = new EmberTraceOptions { Requests = { IgnoredPaths = ["health", "", "/private"] } };

        var middleware = Create(static _ => Task.CompletedTask, options);
        await middleware.InvokeAsync(Request("GET", "/health"));
        await middleware.InvokeAsync(Request("GET", "/private/data"));

        Assert.AreEqual(1, StopAndCollect().Count(e => e.Kind == TraceEventKind.Begin));
    }

    [TestMethod]
    public async Task DisabledRequests_AreNotTracedButStillServed()
    {
        var called = false;
        var options = new EmberTraceOptions { Requests = { Enabled = false } };

        await Create(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, options).InvokeAsync(Request("GET", "/orders/17", "/orders/{id}"));

        Assert.IsTrue(called);
        Assert.IsEmpty(StopAndCollect());
    }

    [TestMethod]
    public async Task StoppedSession_MakesTheMiddlewareTransparent()
    {
        Tracer.Stop();
        var called = false;
        var context = Request("GET", "/orders/17", "/orders/{id}");

        await Create(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }).InvokeAsync(context);

        Assert.IsTrue(called);
        Assert.AreEqual(0L, context.GetEmberTraceFlowId());
    }

    [TestMethod]
    public async Task RecordFlowDisabled_KeepsOnlyTheScope()
    {
        var options = new EmberTraceOptions { Requests = { RecordFlow = false } };
        var context = Request("GET", "/orders/17", "/orders/{id}");

        await Create(static _ => Task.CompletedTask, options).InvokeAsync(context);

        CollectionAssert.AreEqual(
            new[] { TraceEventKind.Begin, TraceEventKind.End },
            StopAndCollect().Select(e => e.Kind).ToArray());
        Assert.AreEqual(0L, context.GetEmberTraceFlowId());
    }

    [TestMethod]
    public async Task Exceptions_PropagateAfterClosingTheFlowAndTheScope()
    {
        var middleware = Create(static _ => throw new InvalidOperationException("boom"));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(Request("GET", "/orders/17", "/orders/{id}")));

        CollectionAssert.AreEqual(
            new[] { TraceEventKind.FlowStart, TraceEventKind.Begin, TraceEventKind.FlowEnd, TraceEventKind.End },
            StopAndCollect().Select(e => e.Kind).ToArray());
    }

    [TestMethod]
    public async Task RouteCardinality_IsCappedToTheFallbackName()
    {
        var options = new EmberTraceOptions { Requests = { MaxTrackedRoutes = 1 } };
        var middleware = Create(static _ => Task.CompletedTask, options);

        await middleware.InvokeAsync(Request("GET", "/a", "/a"));
        await middleware.InvokeAsync(Request("GET", "/b", "/b"));

        CollectionAssert.AreEqual(
            new[] { Tracer.Id("GET /a"), Tracer.Id("HTTP GET") },
            StopAndCollect().Where(e => e.Kind == TraceEventKind.Begin).Select(e => e.Id).ToArray());
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task SlowRequest_IsCapturedEvenWhenItThrows_AndFastOnesAreNot(bool slow)
    {
        var directory = Path.Combine(Path.GetTempPath(), "embertrace-mw-" + Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddEmberTrace(options =>
        {
            options.SlowRequests.Enabled = true;
            options.SlowRequests.Directory = directory;
            options.SlowRequests.Threshold = slow ? TimeSpan.FromMilliseconds(5) : TimeSpan.FromMinutes(1);
            options.SlowRequests.Window = TimeSpan.Zero;
        });

        await using var provider = services.BuildServiceProvider();
        var middleware = new EmberTraceMiddleware(
            static _ =>
            {
                Thread.Sleep(20);
                throw new InvalidOperationException("boom");
            },
            provider.GetRequiredService<IOptionsMonitor<EmberTraceOptions>>());

        var context = Request("GET", "/orders/17", "/orders/{id}");
        context.RequestServices = provider;

        try
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

            var elapsed = Stopwatch.StartNew();
            while (slow && !HasCapture(directory))
            {
                Assert.IsLessThan(TimeSpan.FromSeconds(5), elapsed.Elapsed);
                await Task.Delay(20);
            }

            Assert.AreEqual(slow, HasCapture(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    private static EmberTraceMiddleware Create(RequestDelegate next, EmberTraceOptions? options = null)
    {
        return new EmberTraceMiddleware(next,
            new TestOptionsMonitor<EmberTraceOptions>(options ?? new EmberTraceOptions()));
    }

    private static DefaultHttpContext Request(string method, string path, string? routePattern = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;

        if (routePattern is not null)
            context.SetEndpoint(new RouteEndpoint(
                static _ => Task.CompletedTask,
                RoutePatternFactory.Parse(routePattern),
                0,
                null,
                routePattern));

        return context;
    }

    private static bool HasCapture(string directory)
    {
        return Directory.Exists(directory) && Directory.EnumerateFiles(directory, "*.ember").Any();
    }

    private static List<TraceEventRecord> StopAndCollect()
    {
        return Tracer.Stop().SortedEvents();
    }
}
