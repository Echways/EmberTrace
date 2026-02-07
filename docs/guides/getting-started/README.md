Русская версия: [./README.ru.md](./README.ru.md)

# Quick Start

Goal: capture your first trace, generate a text report, and open it in Chrome Trace.

## Requirements

- .NET SDK: see `global.json` (this repository uses `net10.0`)

## 1) Install packages

### Option A: one package

```bash
dotnet add package EmberTrace.All
```

### Option B: selective install

Minimum for runtime tracing:

```bash
dotnet add package EmberTrace
```

To include **names/categories** in exports and reports:

```bash
dotnet add package EmberTrace.Abstractions
dotnet add package EmberTrace.Generator
```

> `EmberTrace.Generator` is a source generator. It collects `[assembly: TraceId(...)]` and automatically
> registers metadata (via `ModuleInitializer`).

Optional:

```bash
dotnet add package EmberTrace.OpenTelemetry
dotnet add package EmberTrace.RoslynAnalyzers
```

> `EmberTrace.RoslynAnalyzers` includes code fixes. They work in IDE and do not affect CLI builds.

## 2) Define TraceId (metadata)

Add assembly-level attributes (for example in `TraceIds.cs`):

```csharp
using EmberTrace.Abstractions.Attributes;

[assembly: TraceId(1000, "App", "App")]
[assembly: TraceId(2000, "Worker", "Workers")]
[assembly: TraceId(2100, "IoWait", "IO")]
```

Recommendations:
- keep ranges per module/subsystem (for example, `1000-1999`, `2000-2999`...)
- names should be short and stable (they go to reports/export)

## 3) Instrument code

```csharp
using EmberTrace;

Tracer.Start();

using (Tracer.Scope(1000))
{
    // работа
}

var session = Tracer.Stop();
```

Important:
- `Scope` is a `ref struct`: it **cannot** cross `await`. For `async`, use `ScopeAsync`.

```csharp
await using (Tracer.ScopeAsync(2100))
{
    await Task.Delay(50);
}
```

## 4) Report and export

### Text report

```csharp
var processed = session.Process();
var meta = Tracer.CreateMetadata();

Console.WriteLine(TraceText.Write(processed, meta: meta, topHotspots: 20, maxDepth: 8));
```

### Chrome Trace (JSON)

```csharp
Directory.CreateDirectory("out");

using var fs = File.Create("out/trace.json");
TraceExport.WriteChromeComplete(session, fs, meta: meta);
```

Open in:
- `chrome://tracing` (Chrome)
- Perfetto (web UI) - suitable for large traces

## 5) Demo project (recommended)

The repository contains a ready-to-run scenario with scopes + flows + async + export + report:

```bash
dotnet run --project samples/EmberTrace.Demo3 -c Release
# выходные файлы: samples/EmberTrace.Demo3/out
```

Next:
- [Usage and API](../usage/README.md)
- [Flow and async](../../concepts/flows/README.md)
