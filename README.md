Русская версия: [./README.ru.md](./README.ru.md)

# EmberTrace

**EmberTrace** is a fast in-process tracer/profiler for .NET with minimal overhead on the hot path:

- **Allocation-free `Scope`, `Instant`, `Counter` and `FlowStart`/`FlowStep`/`FlowEnd`** with a lock-free hot path (thread-local
  buffers; shared session state is touched only on chunk rotation and when a limit is configured).
  `ScopeAsync` allocates about 224 bytes per scope to flow its parent through `await`; a generated `[Trace]`
  async method pays about half of that
- **Flows** for links between threads and `async/await`
- **Offline analysis** after stopping a session (aggregations + reports)
- **Export to Chrome Trace** (for `chrome://tracing` / Perfetto)
- **Flight recorder** - `Tracer.Snapshot()` dumps the last N seconds from a live session without stopping it
- **Auto-instrumentation** - `[Trace]` on a `partial` method generates the scope wrapper, the id and the metadata;
  `[Trace]` on a class generates a DI decorator for the whole service

## Installation

The easiest option is the metapackage:

```bash
dotnet add package EmberTrace.All
```

If you install packages selectively:

- `EmberTrace` - runtime API (`Tracer.*`)
- `EmberTrace.Abstractions` - attributes (`[assembly: TraceId(...)]`)
- `EmberTrace.Generator` - source generator (automatically registers metadata)
- `EmberTrace.Analysis` - session processing (`session.Process()`)
- `EmberTrace.ReportText` - text report (`TraceText.Write(...)`)
- `EmberTrace.Export` - Chrome Trace export (`TraceExport.*`)
- `EmberTrace.Format` - binary session persistence (`TraceFormat.Write/Read`)
- `EmberTrace.OpenTelemetry` - export to OpenTelemetry (`Activity` spans)
- `EmberTrace.Extensions.Hosting` - ASP.NET Core integration (`AddEmberTrace()`, request middleware, `appsettings.json`, `/embertrace/dump`)
- `EmberTrace.Testing` - performance assertions and baseline diffing for tests (`stats.Scope(...)`, `TraceBudget`)
- `EmberTrace.RoslynAnalyzers` - analyzers and code fixes for correct usage (fixes run in IDE and are included in the
  package)

## Quick Start

1) Define IDs and metadata (in any project file, at the assembly level):

```csharp
using EmberTrace.Abstractions.Attributes;

[assembly: TraceId(1000, "App", "App")]
[assembly: TraceId(2000, "Worker", "Workers")]
```

2) Wrap the required sections in scopes:

```csharp
using EmberTrace;

Tracer.Start();

using (Tracer.Scope(1000))
{
    // work
}

var session = Tracer.Stop();
```

3) Generate a report and/or export:

```csharp
var processed = session.Process();
var meta = Tracer.CreateMetadata(); // if generator is used — metadata will be registered automatically

Console.WriteLine(TraceText.Write(processed, meta: meta, topHotspots: 20, maxDepth: 8));

using var fs = File.Create("out/trace.json");
TraceExport.WriteChromeComplete(session, fs, meta: meta);
```

4) Open `out/trace.json`:

- `chrome://tracing` (Chrome)
- Perfetto (web UI) - convenient for large traces

## Repository Examples

| Sample | Shows |
|--------|-------|
| [`BasicTracing`](samples/EmberTrace.BasicTracing) | scopes, text report with percentiles, Chrome Trace, collapsed stacks, `.ember` round trip |
| [`ParallelWorkers`](samples/EmberTrace.ParallelWorkers) | worker threads, flows with `AnalyzeFlows`, runtime counters |
| [`AutoInstrumentation`](samples/EmberTrace.AutoInstrumentation) | `[Trace]` on `partial` methods |
| [`FlightRecorder`](samples/EmberTrace.FlightRecorder) | `Tracer.Snapshot()` over a bounded `DropOldest` buffer |
| [`WebApi`](samples/EmberTrace.WebApi) | ASP.NET Core hosting, `/embertrace/dump`, slow request capture |
| [`NativeAot`](samples/EmberTrace.NativeAot) | NativeAOT publish |
| [`DocScreenshots`](samples/EmberTrace.DocScreenshots) | every scenario behind the images in `docs/assets` |

```bash
dotnet run --project samples/EmberTrace.BasicTracing -c Release -p:UseLocalEmberTrace=true
# files will be in ./out
```

`-p:UseLocalEmberTrace=true` builds a sample against the sources in this repository instead of the published
packages.

The images and reports in `docs/assets` are regenerated with
`python3 samples/EmberTrace.DocScreenshots/capture.py` (requires Playwright with Chromium and Pygments).

## Documentation

- [Index](docs/index.md)
- [Quick Start](docs/guides/getting-started/README.md)
- [Usage and API](docs/guides/usage/README.md)
- [Flow and async](docs/concepts/flows/README.md)
- [Export](docs/guides/export/README.md)
- [Analysis and reports](docs/guides/analysis/README.md)
- [Flight recorder](docs/guides/flight-recorder/README.md)
- [Generator and metadata](docs/reference/source-generator/README.md)
- [Auto-instrumentation with [Trace]](docs/guides/auto-instrumentation/README.md)
- [Troubleshooting](docs/troubleshooting/README.md)

## Build and Tests

Requires the SDK specified in `global.json`.

```bash
dotnet build -c Release
dotnet test -c Release
```

## Benchmarks and AOT

```bash
dotnet run --project benchmarks/EmberTrace.Benchmarks -c Release -- --filter *ScopeBenchmarks*
```

```bash
dotnet publish samples/EmberTrace.NativeAot -c Release -p:PublishAot=true
```

## Screenshots

**Example of a simple trace in Perfetto**

![Perfetto timeline](docs/assets/getting-started-first-trace.png)

**Runtime counters next to scopes**

![Runtime counters in Perfetto](docs/assets/runtime-counters-timeline.png)

**Flame graph in speedscope**

![Flame graph in speedscope](docs/assets/analysis-flame-graph.png)

## Useful Links

- Documentation: [docs/index.md](docs/index.md)
- Examples: [samples/](samples/)
