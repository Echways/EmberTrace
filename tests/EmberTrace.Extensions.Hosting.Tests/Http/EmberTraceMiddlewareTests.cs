using System.Diagnostics;
using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Extensions.Hosting.Http;
using EmberTrace.Extensions.Hosting.Recording;
using EmberTrace.Sessions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace EmberTrace.Extensions.Hosting.Tests.Http;

[TestClass]
[DoNotParallelize]
public sealed class EmberTraceMiddlewareTests
{
    [TestInitialize]
    public void Setup()
    {
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

    private static EmberTraceMiddleware Create(RequestDelegate next, EmberTraceOptions? options = null)
    {
        return new EmberTraceMiddleware(next, new TestOptionsMonitor<EmberTraceOptions>(options ?? new EmberTraceOptions()));
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

    private static List<TraceEventRecord> StopAndCollect()
    {
        var session = Tracer.Stop();
        var events = new List<TraceEventRecord>();
        foreach (var e in session.EnumerateEventsSorted())
            events.Add(e);

        return events;
    }

    [TestMethod]
    public async Task Request_IsWrappedInAScope()
    {
        var middleware = Create(static _ => Task.CompletedTask);

        await middleware.InvokeAsync(Request("GET", "/orders/17", "/orders/{id}"));

        var expected = Tracer.Id("GET /orders/{id}");
        var events = StopAndCollect();

        Assert.AreEqual(1, events.Count(e => e.Id == expected && e.Kind == TraceEventKind.Begin));
        Assert.AreEqual(1, events.Count(e => e.Id == expected && e.Kind == TraceEventKind.End));
    }

    [TestMethod]
    public async Task Request_RecordsAFlow()
    {
        var middleware = Create(static _ => Task.CompletedTask);

        await middleware.InvokeAsync(Request("GET", "/orders/17", "/orders/{id}"));

        var events = StopAndCollect();

        Assert.AreEqual(1, events.Count(e => e.Kind == TraceEventKind.FlowStart));
        Assert.AreEqual(1, events.Count(e => e.Kind == TraceEventKind.FlowEnd));
    }

    [TestMethod]
    public async Task FlowId_IsPublishedOnTheContext()
    {
        long observed = 0;
        var middleware = Create(context =>
        {
            observed = context.GetEmberTraceFlowId();
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(Request("GET", "/orders/17", "/orders/{id}"));

        Assert.AreNotEqual(0L, observed);
    }

    [TestMethod]
    public async Task FlowId_FollowsTheCurrentActivity()
    {
        using var activity = new Activity("request");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();

        long observed = 0;
        var middleware = Create(context =>
        {
            observed = context.GetEmberTraceFlowId();
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(Request("GET", "/orders/17", "/orders/{id}"));

        Assert.AreEqual(
            EmberTrace.ActivityBridge.ActivityBridge.FlowIdFromTraceId(activity.TraceId.ToHexString()),
            observed);
    }

    [TestMethod]
    public async Task WithoutAnEndpoint_TheMethodIsUsed()
    {
        var middleware = Create(static _ => Task.CompletedTask);

        await middleware.InvokeAsync(Request("POST", "/anything"));

        var expected = Tracer.Id("HTTP POST");
        var events = StopAndCollect();

        Assert.IsTrue(events.Any(e => e.Id == expected && e.Kind == TraceEventKind.Begin));
    }

    [TestMethod]
    public async Task IgnoredPaths_AreNotTraced()
    {
        var middleware = Create(static _ => Task.CompletedTask);

        await middleware.InvokeAsync(Request("GET", "/health/ready"));
        await middleware.InvokeAsync(Request("GET", "/embertrace/dump"));

        Assert.AreEqual(0, StopAndCollect().Count);
    }

    [TestMethod]
    public async Task DisabledRequests_AreNotTraced()
    {
        var options = new EmberTraceOptions { Requests = new EmberTraceRequestOptions { Enabled = false } };
        var middleware = Create(static _ => Task.CompletedTask, options);

        await middleware.InvokeAsync(Request("GET", "/orders/17", "/orders/{id}"));

        Assert.AreEqual(0, StopAndCollect().Count);
    }

    [TestMethod]
    public async Task RecordFlowDisabled_KeepsTheScope()
    {
        var options = new EmberTraceOptions { Requests = new EmberTraceRequestOptions { RecordFlow = false } };
        var middleware = Create(static _ => Task.CompletedTask, options);

        await middleware.InvokeAsync(Request("GET", "/orders/17", "/orders/{id}"));

        var events = StopAndCollect();

        Assert.AreEqual(2, events.Count);
        Assert.IsFalse(events.Any(e => e.Kind == TraceEventKind.FlowStart));
    }

    [TestMethod]
    public async Task Exceptions_PropagateAndCloseTheScope()
    {
        var middleware = Create(static _ => throw new InvalidOperationException("boom"));

        try
        {
            await middleware.InvokeAsync(Request("GET", "/orders/17", "/orders/{id}"));
            Assert.Fail("Expected InvalidOperationException.");
        }
        catch (InvalidOperationException)
        {
        }

        var expected = Tracer.Id("GET /orders/{id}");
        var events = StopAndCollect();

        Assert.AreEqual(1, events.Count(e => e.Id == expected && e.Kind == TraceEventKind.End));
    }

    [TestMethod]
    public async Task WhenTheSessionIsStopped_TheMiddlewareIsTransparent()
    {
        Tracer.Stop();
        var called = false;
        var middleware = Create(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(Request("GET", "/orders/17", "/orders/{id}"));

        Assert.IsTrue(called);
    }

    [TestMethod]
    public async Task RouteCardinality_IsCapped()
    {
        var options = new EmberTraceOptions
        {
            Requests = new EmberTraceRequestOptions { MaxTrackedRoutes = 1 }
        };
        var middleware = Create(static _ => Task.CompletedTask, options);

        await middleware.InvokeAsync(Request("GET", "/a", "/a"));
        await middleware.InvokeAsync(Request("GET", "/b", "/b"));

        var events = StopAndCollect();

        Assert.IsTrue(events.Any(e => e.Id == Tracer.Id("GET /a") && e.Kind == TraceEventKind.Begin));
        Assert.IsTrue(events.Any(e => e.Id == Tracer.Id("HTTP GET") && e.Kind == TraceEventKind.Begin));
    }
}
