English version: [./README.md](./README.md)

# Тесты производительности и гейты регрессий

`EmberTrace.Testing` превращает трассу в проверки, которые можно гонять в CI. Пакет не зависит от тестового
фреймворка — любая неудача это `TraceAssertionException`, который MSTest, xUnit и NUnit одинаково показывают как
упавший тест.

```bash
dotnet add package EmberTrace.Testing
```

## Проверка бюджета

```csharp
Tracer.Start();
RunTheScenario();
var stats = Tracer.Stop().Analyze();

stats.Scope(Ids.DbQuery, meta)
    .CountAtMost(3)
    .P95MsUnder(5)
    .P99MsUnder(12);
```

Сообщение об ошибке называет scope и оба числа:

```
Trace assertion failed: expected 'DbQuery' (id 2100) p95 to be under 5.000 ms, but it was 8.240 ms.
```

Доступные проверки: `CountExactly`, `CountAtMost`, `CountAtLeast`, `NotRecorded`, `TotalMsUnder`, `AverageMsUnder`,
`MaxMsUnder`, `P50MsUnder`, `P95MsUnder`, `P99MsUnder` и `PercentileMsUnder(percentile, ms)` для всего остального.

Любая пороговая проверка падает, если id вообще не встретился в трассе; для обратного утверждения есть `NotRecorded()`.
Вызовы можно свободно чейнить — каждый возвращает ту же проверку.

## Сравнение с базовой линией

Абсолютные пороги по перцентилям хрупки при переезде между машинами. Обычно надёжнее сравнивать два прогона:

```csharp
var comparison = TraceDiff.Compare(baselineStats, currentStats);
Console.WriteLine(TraceDiff.Format(comparison, meta, minPercent: 5));

TraceBudget.AssertNoRegressions(baselineStats, currentStats, maxPercent: 10, meta);
```

`Compare` возвращает все id из обоих прогонов, худшая регрессия по суммарному времени — первой. `RegressionsOver(percent)`
и `AssertNoRegressions` смотрят только на id, записанные в *обоих* прогонах: появившийся или исчезнувший id виден
в `Format` (пометки `(new)` и `(gone)`), но гейт не роняет — сравнивать его не с чем.

Если базовое значение равно нулю, а текущее положительно, изменение равно `PositiveInfinity`; для id, присутствующего
только с одной стороны, — `NaN`.

## Хранение базовых линий

Сохрани записанную сессию через `EmberTrace.Format` и проанализируй её позже:

```csharp
TraceFormat.Write(session, "baselines/checkout.ember");

var baseline = TraceFormat.Read("baselines/checkout.ember").Analyze();
```

## Точность перцентилей

Длительности раскладываются по лог-линейной гистограмме с 5 значащими битами, поэтому перцентиль отличается от точного
не больше чем на 3.125% и всегда округляется **вверх** — гейт не пройдёт из-за заниженной задержки. Длительности ниже
64 тиков записываются точно, `MinMs` и `MaxMs` всегда точные.

См. также:
- [Анализ и отчёты](../analysis/README.ru.md)
- [Бинарный формат сессии (.ember)](../format/README.ru.md)
