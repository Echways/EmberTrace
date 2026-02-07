Русская версия: [./README.ru.md](./README.ru.md)

# Analysis and reports

After `Tracer.Stop()`, you can perform heavy trace processing: compute aggregates and print a report.

## Processing

```csharp
var session = Tracer.Stop();
var processed = session.Process();
```

`Process()` builds aggregates by id and call tree (by threads), which are convenient to:
- print in a report
- compare across runs
- use in your own tools
`ProcessedTrace` also stores dropped/sampled counters and stack errors.

Additional modes:

```csharp
var processed = session.Process(strict: true, groupByThread: false);
```

- `strict` - does not attempt stack repair for mismatched end
- `groupByThread` - when `false`, builds a global call tree

For lightweight diagnostics:

```csharp
var stats = session.Analyze(strict: true);
```

Flow chain analysis is also available:

```csharp
var flows = session.AnalyzeFlows(top: 10);
```

## Text report

```csharp
var meta = Tracer.CreateMetadata();

var text = TraceText.Write(
    processed,
    meta: meta,
    topHotspots: 20,
    maxDepth: 8,
    categoryFilter: "IO",
    minPercent: 1);

Console.WriteLine(text);
```

Parameters:
- `topHotspots` - number of hotspot lines to show
- `maxDepth` - call tree depth
- `categoryFilter` - category filter
- `minPercent` - minimum percentage to display

See also:
- [Export](../export/README.md)
- [Usage and API](../usage/README.md)

## Screenshots

![Срез анализа: агрегирование/сортировка/фильтры](../../assets/analysis-slice.png)

## Links

- [**Analysis slice**](../../assets/analysis-slice.txt)
