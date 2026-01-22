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

## Текстовый отчёт

```csharp
var meta = Tracer.CreateMetadata();

var text = TraceText.Write(
    processed,
    meta: meta,
    topHotspots: 20,
    maxDepth: 8);

Console.WriteLine(text);
```

Параметры:
- `topHotspots` — сколько строк «горячих точек» показать
- `maxDepth` — глубина дерева вызовов

См. также:
- [Экспорт](../export/README.md)
- [Использование и API](../usage/README.md)

## Скриншоты

![Срез анализа: агрегирование/сортировка/фильтры](../../assets/analysis-slice.png)

## Ссылки

- [**Analysis slice**](../../assets/analysis-slice.txt)
