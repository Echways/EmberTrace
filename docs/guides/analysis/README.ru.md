English version: [./README.md](./README.md)

# Анализ и отчёты

После `Tracer.Stop()` можно «тяжело» обработать трассу: посчитать агрегаты и вывести отчёт.

## Обработка

```csharp
var session = Tracer.Stop();
var processed = session.Process();
```

`Process()` строит агрегаты по id и call-tree (по потокам), которые удобно:
- печатать в отчёте
- сравнивать между прогонами
- использовать в своих тулзах
В `ProcessedTrace` также сохраняются счётчики dropped/sampled и ошибки стека.

Дополнительные режимы:

```csharp
var processed = session.Process(strict: true, groupByThread: false);
```

- `strict` — не пытается «ремонтировать» стек при mismatched end
- `groupByThread` — если `false`, строится общий call tree

Один `ThreadTrace` — это одна дорожка писателя, а не один managed thread id: `TrackId` — то, по чему
сгруппировано дерево, `ThreadId` — поток, который его писал, и нужен для отображения. Поэтому две
записи могут иметь одинаковый `ThreadId`, если рантайм переиспользовал его в течение сессии, а
`ThreadsSeen` считает дорожки и в этом случае не занижает результат.

Для лёгкой диагностики:

```csharp
var stats = session.Analyze(strict: true);
```

Также доступен анализ flow‑цепочек:

```csharp
var flows = session.AnalyzeFlows(top: 10);
```

## Перцентили

`Analyze()` прикладывает к каждому id распределение длительностей:

```csharp
var stats = session.Analyze();

foreach (var row in stats.ByTotalTimeDesc)
    Console.WriteLine($"{row.Id}: p50={row.P50Ms:F3} p95={row.P95Ms:F3} p99={row.P99Ms:F3} max={row.MaxMs:F3}");
```

`row.Durations` — это сама гистограмма `DurationHistogram`, если нужен произвольный перцентиль:
`row.Durations.PercentileTicks(99.9)`.

`Process()` прикладывает то же распределение к каждой строке `HotspotRow` как `P50Ms`, `P95Ms` и `P99Ms`.

Длительности раскладываются по корзинам с 5 значащими битами: относительная погрешность не больше
3.125%, округление всегда вверх, ниже 64 тиков — точно. `MinMs` и `MaxMs` всегда точные.

## Текстовый отчёт

```csharp
var meta = Tracer.CreateMetadata();

var text = TraceText.Write(
    processed,
    meta: meta,
    topHotspots: 20,
    maxDepth: 8,
    categoryFilter: "IO",
    minPercent: 1,
    includePercentiles: true);

Console.WriteLine(text);
```

Параметры:
- `topHotspots` — сколько строк «горячих точек» показать
- `maxDepth` — глубина дерева вызовов
- `categoryFilter` — фильтр по категории
- `minPercent` — минимальный процент для вывода
- `includePercentiles` — добавляет колонки p50/p95/p99 в таблицу «горячих точек»

См. также:
- [Экспорт](../export/README.ru.md)
- [Использование и API](../usage/README.ru.md)

## Скриншоты

![Срез анализа: агрегирование/сортировка/фильтры](../../assets/analysis-slice.png)

## Ссылки

- [**Analysis slice**](../../assets/analysis-slice.txt)
