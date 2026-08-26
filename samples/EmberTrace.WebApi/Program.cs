using EmberTrace;
using EmberTrace.Extensions.Hosting.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEmberTrace();

var app = builder.Build();

app.UseRouting();
app.UseEmberTrace();

var work = Tracer.Id("Orders.Load");
var slow = Tracer.Id("Orders.Slow");

app.MapGet("/orders/{id:int}", async (int id, HttpContext context) =>
{
    await using (Tracer.ScopeAsync(work))
    {
        await Task.Delay(Random.Shared.Next(1, 5));

        if (id % 10 == 0)
            await using (Tracer.ScopeAsync(slow))
            {
                await Task.Delay(40);
            }
    }

    return Results.Ok(new { id, flowId = context.GetEmberTraceFlowId() });
});

app.MapGet("/health", () => Results.Ok("ok"));

app.MapEmberTraceDump();

app.Run();
