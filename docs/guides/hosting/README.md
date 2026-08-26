Русская версия: [./README.ru.md](./README.ru.md)

# Hosting integration (ASP.NET Core)

`EmberTrace.Extensions.Hosting` turns the flight recorder into something you configure instead of
something you code: the session follows the host lifetime, every request becomes a scope and a flow,
and a guarded endpoint hands you the last N seconds as a file.

```bash
dotnet add package EmberTrace.Extensions.Hosting
```

The package framework-references `Microsoft.AspNetCore.App`, so it belongs in web applications. A
worker service can use it too, but it will then require the ASP.NET Core runtime.

## Wiring

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEmberTrace();

var app = builder.Build();

app.UseRouting();
app.UseEmberTrace();
app.MapEmberTraceDump();

app.Run();
```

`UseEmberTrace()` must come **after** `UseRouting()`. Routing is what attaches the endpoint to the
request, and the endpoint is where the route pattern comes from. Placed earlier, the middleware still
works, but every request is recorded as `HTTP GET` instead of `GET /orders/{id}`.

`AddEmberTrace()` binds the `EmberTrace` section, registers the recorder and a hosted service, and
validates the configuration at startup. Alternative forms:

```csharp
builder.Services.AddEmberTrace(options => options.MaxRetentionWindow = TimeSpan.FromSeconds(60));
builder.Services.AddEmberTrace("Tracing");
builder.Services.AddEmberTrace(builder.Configuration.GetSection("Tracing"));
```

## Configuration

```json
{
  "EmberTrace": {
    "Enabled": true,
    "ChunkCapacity": 16384,
    "MaxTotalEvents": 0,
    "MaxTotalChunks": 256,
    "MaxRetentionWindow": "00:00:30",
    "OverflowPolicy": "DropOldest",
    "EnableRuntimeMetadata": true,
    "RuntimeCounters": "Gc, Memory",
    "RuntimeCounterInterval": "00:00:00.050",
    "SampleEveryNGlobal": 0,
    "MaxEventsPerSecond": 0,
    "EnabledCategories": [],
    "DisabledCategories": [],
    "ShutdownDumpDirectory": null,
    "Requests": {
      "Enabled": true,
      "UseRoutePattern": true,
      "RecordFlow": true,
      "Category": "Http",
      "MaxTrackedRoutes": 1024,
      "IgnoredPaths": [ "/health", "/healthz", "/embertrace" ]
    },
    "Dump": {
      "Enabled": false,
      "Path": "/embertrace/dump",
      "ApiKey": null,
      "AuthorizationPolicy": null,
      "RestrictToLoopback": true,
      "AllowAnonymous": false,
      "Window": "00:00:10",
      "MaxWindow": "00:05:00",
      "FileNamePrefix": "embertrace"
    }
  }
}
```

The defaults are flight-recorder defaults: `DropOldest` with a 30-second retention window and a
256-chunk cap. `MaxRetentionWindow` only works with `DropOldest`; any other policy fails validation at
startup with an explicit message rather than throwing from `Tracer.Start`.

Categories are configured by name and hashed into ids the same way `Tracer.CategoryId` does, so
`"EnabledCategories": [ "Http" ]` records requests and nothing else.

## What the middleware records

For each request that is not on `IgnoredPaths`:

- an async scope named `"{METHOD} {route pattern}"`, e.g. `GET /orders/{id}`; ids are bounded by
  `MaxTrackedRoutes`, and once that cap is reached new routes collapse onto `HTTP {METHOD}`;
- a flow that starts before the pipeline and ends after it. When `Activity.Current` is a W3C activity,
  the flow id is derived from its trace id, so the same flow id identifies that request in every
  service that took part in the distributed trace.

The flow id is published on the request:

```csharp
using EmberTrace.Extensions.Hosting.Http;

app.MapGet("/orders/{id:int}", (int id, HttpContext context) =>
{
    var flowId = context.GetEmberTraceFlowId();
    _ = Task.Run(() =>
    {
        Tracer.FlowStep(Tracer.Id("Orders.Background"), flowId);
    });

    return Results.Ok(id);
});
```

## The dump endpoint

`MapEmberTraceDump()` exposes `GET /embertrace/dump`. It is **disabled by default** and refuses to
start unenforced: with `Dump:Enabled` set, the configuration must also carry an `ApiKey`, an
`AuthorizationPolicy`, `RestrictToLoopback` (the default), or an explicit `AllowAnonymous`.

```bash
curl -OJ "http://localhost:5080/embertrace/dump?window=10"
curl -H "X-EmberTrace-Key: $KEY" "http://localhost:5080/embertrace/dump?window=00:00:30&format=chrome" > trace.json
```

- `window` accepts seconds (`10`) or a `TimeSpan` (`00:00:30`), and is clamped to `Dump:MaxWindow`.
  `window=0` means "everything the buffer holds".
- `format` is `ember` (default, binary, readable with `TraceFormat.Read`) or `chrome`
  (Chrome Trace JSON, openable in Perfetto).
- Responses carry `X-EmberTrace-Events` and `X-EmberTrace-Dropped`.

Status codes: `404` when disabled or when the caller fails the loopback restriction — the endpoint
does not advertise itself; `401` on a missing or wrong key; `503` when no session is running; `400`
for an unknown `format`.

To sit behind your own authentication instead:

```json
"Dump": { "Enabled": true, "RestrictToLoopback": false, "AuthorizationPolicy": "Diagnostics" }
```

The policy is applied as an endpoint convention, exactly as `RequireAuthorization("Diagnostics")`
would be.

## Session lifetime

The hosted service starts the session on host start and stops it on host stop. If a session is
already running — a test host, a second in-process host, or application code that called
`Tracer.Start` itself — EmberTrace logs a warning, attaches to it, and does **not** stop it on
shutdown. Ownership is what decides who calls `Tracer.Stop`.

Set `ShutdownDumpDirectory` to have the final session written there as
`{prefix}-shutdown-{timestamp}.ember` during graceful shutdown. A failed write is logged, never
thrown.

## Related

- [Flight recorder (live snapshots)](../flight-recorder/README.md)
- [Runtime counters](../runtime-counters/README.md)
- [Binary session format (.ember)](../format/README.md)
