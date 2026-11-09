# EmberTrace documentation

[English](README.md) · [Русский](README.ru.md) · [Project page](../README.md)

EmberTrace is an in-process tracer and profiler for .NET with an allocation-free hot path. These pages describe every part of it.

## Start

| Page | What is in it |
| ---- | ------------- |
| [Quick start](guides/getting-started/README.md) | Installation, ids, the first scope, a report and a trace file. |
| [Usage and API](guides/usage/README.md) | Sessions, scopes, ids, options, measuring short operations. |
| [Auto-instrumentation](guides/auto-instrumentation/README.md) | `[Trace]` on methods and classes: wrappers, ids, the DI decorator. |

## Recording

| Page | What is in it |
| ---- | ------------- |
| [Flows](concepts/flows/README.md) | Links between threads and `async/await`. |
| [Runtime counters](guides/runtime-counters/README.md) | GC, memory, thread pool and exceptions on the same timeline. |
| [Flight recorder](guides/flight-recorder/README.md) | A bounded window of the recent past, dumped without stopping the session. |

## Output and analysis

| Page | What is in it |
| ---- | ------------- |
| [Export](guides/export/README.md) | Chrome Trace and Perfetto files, captured windows. |
| [Analysis and reports](guides/analysis/README.md) | Aggregates, percentiles, text reports, run comparison, flame graphs. |
| [Session format](guides/format/README.md) | The binary `.ember` file: saving, streaming, compatibility. |
| [Performance testing](guides/testing/README.md) | Budgets and regression gates for CI. |

## Integrations

| Page | What is in it |
| ---- | ------------- |
| [ASP.NET Core hosting](guides/hosting/README.md) | Request scopes, the dump endpoint, slow request capture. |
| [OpenTelemetry](reference/opentelemetry/README.md) | Sessions as `Activity` spans. |
| [Roslyn analyzers](reference/roslyn-analyzers/README.md) | `ETA` diagnostics and code fixes. |

## Reference

| Page | What is in it |
| ---- | ------------- |
| [API](reference/api/index.md) | `Tracer` and `ScopeReader`. |
| [Generator and metadata](reference/source-generator/README.md) | `TraceId`, `TraceName`, generated providers, `ETG` diagnostics. |
| [Session options](reference/session-options/README.md) | Every `SessionOptions` member. |

## Going further

| Page | What is in it |
| ---- | ------------- |
| [Troubleshooting](troubleshooting/README.md) | Symptoms, causes and fixes. |
| [Docs layout](STRUCTURE.md) | How these pages are organized. |
