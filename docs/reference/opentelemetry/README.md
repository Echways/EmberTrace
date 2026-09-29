Русская версия: [./README.ru.md](./README.ru.md)

# OpenTelemetry export

Package `EmberTrace.OpenTelemetry` lets you convert an EmberTrace session into `Activity` spans.

## Installation

```bash
dotnet add package EmberTrace.OpenTelemetry
```

## Example

```csharp
using EmberTrace.OpenTelemetry;

var session = Tracer.Stop();
var spans = OpenTelemetryExport.CreateSpans(session);

foreach (var span in spans)
{
    // send to your own exporter
}
```

## Options

`OpenTelemetryExportOptions`:
- `IncludeFlowsAsLinks` - include Flow as links
- `IncludeThreadIdTag` - include `thread.id`
- `BaseUtc` - UTC time of the session start. Defaults to `TraceSession.StartedAtUtc`: `Tracer.Start` records it for
  the session, and a snapshot derives its own from the wall clock at the moment it is cut, so its spans line up with
  logs written at that moment. It is stored in `.ember` files; only sessions without it fall back to "now minus the
  session duration".

## Notes

- Export does not require OpenTelemetry SDK; plain `Activity` objects are returned.
- Flow is converted to `ActivityLink` to preserve cross-thread relationships.
