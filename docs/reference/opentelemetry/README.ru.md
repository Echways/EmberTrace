English version: [./README.md](./README.md)

# OpenTelemetry export

Пакет `EmberTrace.OpenTelemetry` позволяет конвертировать сессию EmberTrace в `Activity`‑спаны.

## Установка

```bash
dotnet add package EmberTrace.OpenTelemetry
```

## Пример

```csharp
using EmberTrace.OpenTelemetry;

var session = Tracer.Stop();
var spans = OpenTelemetryExport.CreateSpans(session);

foreach (var span in spans)
{
    // отправка в свой экспортёр
}
```

## Опции

`OpenTelemetryExportOptions`:
- `IncludeFlowsAsLinks` — добавить Flow как links
- `IncludeThreadIdTag` — добавить `thread.id`
- `BaseUtc` — UTC‑время начала сессии. По умолчанию берётся `TraceSession.StartedAtUtc`, который записывает
  `Tracer.Start` и сохраняет `.ember`; только сессии без него откатываются к «сейчас минус длительность сессии».

## Примечания

- Экспорт не требует OpenTelemetry SDK; возвращаются обычные `Activity`.
- Flow превращается в `ActivityLink`, чтобы сохранить связи между потоками.
