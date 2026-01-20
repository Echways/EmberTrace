# EmberTrace (runtime)

Runtime библиотека, которая:
- записывает события begin/end в thread-local буферы,
- собирает их в сессию,
- обрабатывает сессию в агрегаты (self/total, call tree),
- умеет выдавать текстовые отчёты и экспорт.

## Публичные точки входа
- `Tracer` — управление сессией и создание scope.
- `Scope` — low-overhead измерение участка кода.
- `SessionOptions` — настройки (размер чанка, overflow policy, флаги).
- `TraceSession` — сырые данные сессии (чанки событий + метаданные времени).

## Слои внутри библиотеки

### Public/
API, который использует потребитель библиотеки.

### Internal/Time/
Единый источник времени:
- `Stopwatch.GetTimestamp()` в hot path,
- конвертация ticks -> time units в отчётах.

### Internal/Buffering/
Низкооверхедная запись:
- `TraceEvent` — фиксированный struct.
- `ThreadWriter` — запись событий в текущий chunk потока.
- `Chunk` — буфер событий фиксированного размера.
- `SessionCollector` — принимает заполненные чанки.
- `ChunkPool` — переиспользование буферов.
- `OverflowPolicy` — поведение при переполнении (drop/stop/overwrite).

### Internal/Metadata/
Связь с generated метаданными:
- контракт (`ITraceMetadata`)
- provider (`TraceMetadataProvider`), который отдаёт таблицу имён/категорий для отчётов.
  Метаданные используются только в cold path.

### Processing/
Постобработка:
- построение интервалов по begin/end,
- call tree по потокам,
- расчёт total/self/call count,
- подготовка таблиц “hotspots”.

### Reporting/
Вывод:
- `TextReportWriter` — читаемый отчёт в консоль/файл.
- `ChromeTraceExporter` — экспорт для визуализации в chrome tracing.

## Принципы
- Hot path не трогает строки и коллекции общего доступа.
- Всё тяжёлое делается после `Stop()`.
- Потоки независимы: запись только в thread-local.
