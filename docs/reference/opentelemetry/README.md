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
    // отправка в свой экспортёр
}
```

## Options

`OpenTelemetryExportOptions`:
- `IncludeFlowsAsLinks` - include Flow as links
- `IncludeThreadIdTag` - include `thread.id`
- `BaseUtc` - base UTC time for timestamp conversion

## Notes

- Export does not require OpenTelemetry SDK; plain `Activity` objects are returned.
- Flow is converted to `ActivityLink` to preserve cross-thread relationships.
