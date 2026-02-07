# EmberTrace

**EmberTrace** — быстрый *in-process* tracer/profiler для .NET с минимальной нагрузкой на горячем пути:
- **Begin/End без аллокаций** и без глобальных lock (thread-local буферы)
- **Flows** для связей между потоками и `async/await`
- **Offline-анализ** после остановки сессии (агрегации + отчёты)
- **Экспорт в Chrome Trace** (для `chrome://tracing` / Perfetto)

## Установка

Самый простой вариант — метапакет:

```bash
dotnet add package EmberTrace.All
```

Если подключаешь выборочно:

- `EmberTrace` — runtime API (`Tracer.*`)
- `EmberTrace.Abstractions` — атрибуты (`[assembly: TraceId(...)]`)
- `EmberTrace.Generator` — source generator (автоматически регистрирует метаданные)
- `EmberTrace.Analysis` — обработка сессии (`session.Process()`)
- `EmberTrace.ReportText` — текстовый отчёт (`TraceText.Write(...)`)
- `EmberTrace.Export` — Chrome Trace export (`TraceExport.*`)
- `EmberTrace.OpenTelemetry` — экспорт в OpenTelemetry (`Activity`-спаны)
- `EmberTrace.RoslynAnalyzers` — анализаторы и code fixes для корректного использования (фиксы работают в IDE и идут в составе пакета)

## Быстрый старт

1) Опиши id и метаданные (в любом файле проекта, *на уровне assembly*):

```csharp
using EmberTrace.Abstractions.Attributes;

[assembly: TraceId(1000, "App", "App")]
[assembly: TraceId(2000, "Worker", "Workers")]
```

2) Оберни нужные участки в scopes:

```csharp
using EmberTrace;

Tracer.Start();

using (Tracer.Scope(1000))
{
    // работа
}

var session = Tracer.Stop();
```

3) Сними отчёт и/или экспорт:

```csharp
var processed = session.Process();
var meta = Tracer.CreateMetadata(); // если подключён generator — метаданные будут зарегистрированы автоматически

Console.WriteLine(TraceText.Write(processed, meta: meta, topHotspots: 20, maxDepth: 8));

using var fs = File.Create("out/trace.json");
TraceExport.WriteChromeComplete(session, fs, meta: meta);
```

4) Открой `out/trace.json`:
- `chrome://tracing` (Chrome)
- Perfetto (веб-UI) — удобно для больших трасс

## Пример из репозитория

Самый полный пример — `samples/EmberTrace.Demo3` (scopes + flows + async + экспорт + текстовый отчёт):

```bash
dotnet run --project samples/EmberTrace.Demo3 -c Release
# файлы появятся в samples/EmberTrace.Demo3/out
```

## Документация

- [Индекс](docs/index.md)
- [Быстрый старт](docs/guides/getting-started/README.md)
- [Использование и API](docs/guides/usage/README.md)
- [Flow и async](docs/concepts/flows/README.md)
- [Экспорт](docs/guides/export/README.md)
- [Анализ и отчёты](docs/guides/analysis/README.md)
- [Генератор и метаданные](docs/reference/source-generator/README.md)
- [Устранение неполадок](docs/troubleshooting/README.md)

## Сборка и тесты

Требуется SDK, указанный в `global.json`.

```bash
dotnet build -c Release
dotnet test -c Release
```

## Бенчмарки и AOT

```bash
dotnet run --project benchmarks/EmberTrace.Benchmarks -c Release -- --filter *ScopeBenchmarks*
```

```bash
dotnet publish samples/EmberTrace.NativeAot -c Release -p:PublishAot=true
```

## Скриншоты

**Пример простой трассы в Perfetto**

![Perfetto timeline](docs/assets/getting-started-first-trace.png)

## Полезные ссылки

- Документация: [docs/index.md](docs/index.md)
- Примеры: [samples/](samples/)
