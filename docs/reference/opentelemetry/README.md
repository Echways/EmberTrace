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
- `BaseUtc` — базовое UTC‑время для конвертации timestamp

## Примечания

- Экспорт не требует OpenTelemetry SDK; возвращаются обычные `Activity`.
- Flow превращается в `ActivityLink`, чтобы сохранить связи между потоками.
