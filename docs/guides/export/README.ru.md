English version: [./README.md](./README.md)

# Экспорт

EmberTrace экспортирует сессию в JSON-формат Chrome Trace, который понимают:
- `chrome://tracing`
- Perfetto (веб-UI)

## WriteChromeComplete

Рекомендуемый формат: «complete events» (длительности упакованы в один объект).

```csharp
var session = Tracer.Stop();
var meta = Tracer.CreateMetadata();

Directory.CreateDirectory("out");
using var fs = File.Create("out/trace_complete.json");

TraceExport.WriteChromeComplete(session, fs, meta: meta);
```

Порядок событий стабилен: timestamp → track → sequence.

## WriteChromeBeginEnd

Альтернатива: отдельные Begin/End события (может быть удобнее для некоторых тулов).

```csharp
using var fs = File.Create("out/trace_beginend.json");
TraceExport.WriteChromeBeginEnd(session, fs, meta: meta);
```

Экспорт включает:
- Flow (`FlowStart/Step/End`)
- Instant и Counter
- имена потоков (если заданы через `Thread.CurrentThread.Name`)

Chrome-поле `tid` — это id дорожки писателя, а не managed thread id: дорожка соответствует одному потоку
в пределах одной сессии, поэтому поток, который умер и отдал свой `ManagedThreadId` новому потоку,
получает отдельную строку, а не склеенную. Managed thread id используется для имени строки — через
метаданные `thread_name`.

## MarkedComplete: снять короткое окно

Полезно, когда не хочешь вручную управлять `Start/Stop` вокруг небольшого участка.

```csharp
TraceExport.MarkedComplete(
    "WarmPath",
    () =>
    {
        using var _ = Tracer.Scope(Ids.App);
        Work();
    },
    new MarkedCompleteOptions { OutputPath = "out/warm_path.json" });
```

Всё остальное задаётся через `MarkedCompleteOptions`: `Unique` добавляет к имени номер строки
вызова, `Pid` и `ProcessName` подписывают процесс во вьюере. Без `OutputPath` файл попадает в
`traces/<имя>_<utc-время>.json`. `MarkedCompleteAsync` принимает тело `Func<Task>`.

## SliceAndResume: окно внутри уже идущей сессии

Если сессия уже запущена, `MarkedComplete` бросает исключение, пока явно не попросить срезать её:

```csharp
var result = TraceExport.MarkedComplete(
    "Slice",
    () =>
    {
        using var _ = Tracer.Scope(Ids.App);
        Work();
    },
    new MarkedCompleteOptions
    {
        OutputPath = "out/slice.json",
        Running = MarkedRunningSessionMode.SliceAndResume
    });

result.SaveFullChromeComplete("out/slice_full.json", meta: Tracer.CreateMetadata());
```

Идущая сессия не останавливается: окно вырезается из снапшота, поэтому flight recorder сохраняет
историю, а другие потоки продолжают запись, пока выполняется тело. `result.CapturedSession` — это
тот самый снапшот (`IsSnapshot` равно `true`).

Чтобы работать со своей сессией, а не с глобальным `Tracer`, передай её в опциях:

```csharp
using var tracing = new TracingSession();

TraceExport.MarkedComplete("Import", () => Import(tracing),
    new MarkedCompleteOptions { Session = tracing, OutputPath = "out/import.json" });
```

## Переход с 0.3

Перегрузки, помеченные устаревшими в 0.3, удалены в 0.4. Каждая из них выражается через вызов с опциями:

| 0.3 | 0.4 |
|---|---|
| `MarkedComplete(name, outputPath, body)` | `MarkedComplete(name, body, new MarkedCompleteOptions { OutputPath = outputPath })` |
| `MarkedComplete(name, body)` | `MarkedComplete(name, body, default)` |
| `MarkedCompleteEx(...)` | `MarkedComplete(...)` — он и так возвращает `MarkedCompleteResult` |
| `MarkedCompleteUnique(...)`, `MarkedCompleteExUnique(...)` | `new MarkedCompleteOptions { Unique = true }` |
| аргументы `running:`, `pid:`, `processName:` | свойства `Running`, `Pid`, `ProcessName` |
| перегрузки без имени (имя бралось из вызывающего метода) | передай `nameof(ВызывающийМетод)` |
| возвращаемый `TraceSession` | `result.CapturedSession` |
| варианты `...Async` | те же замены на `MarkedCompleteAsync` |
| аргумент `resumeOptions:` / свойство `ResumeOptions` | удалено — сессия больше не перезапускается |

См. также:
- [Анализ и отчёты](../analysis/README.ru.md)
- [Flow и async](../../concepts/flows/README.ru.md)

## Скриншоты

![Открытый экспорт: json открыт в Perfetto](../../assets/export-opened.png)

