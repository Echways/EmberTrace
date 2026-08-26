using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Extensions.Hosting.Recording;
using EmberTrace.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EmberTrace.Extensions.Hosting.Tests.Http;

[TestClass]
[DoNotParallelize]
public sealed class UseEmberTraceTests
{
    [TestInitialize]
    public void Setup()
    {
        HttpTraceIds.Clear();
        HttpTraceIds.EnsureRegistered();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Tracer.IsRunning)
            Tracer.Stop();

        HttpTraceIds.Clear();
    }

    [TestMethod]
    public async Task PipelineTracesTheRequest()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddEmberTrace();
        using var provider = services.BuildServiceProvider();

        var app = new ApplicationBuilder(provider);
        app.UseEmberTrace();
        app.Run(static context =>
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        var pipeline = app.Build();

        Tracer.Start(SessionOptionsFactory.Create(new EmberTraceOptions()));

        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Method = "GET";
        context.Request.Path = "/orders/17";
        context.SetEndpoint(new RouteEndpoint(
            static _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/orders/{id}"),
            0,
            null,
            "orders"));

        await pipeline(context);

        var session = Tracer.Stop();
        var expected = Tracer.Id("GET /orders/{id}");
        var begins = 0;
        foreach (var e in session.EnumerateEvents())
            if (e.Id == expected && e.Kind == TraceEventKind.Begin)
                begins++;

        Assert.AreEqual(1, begins);
        Assert.AreEqual(StatusCodes.Status204NoContent, context.Response.StatusCode);
    }
}
