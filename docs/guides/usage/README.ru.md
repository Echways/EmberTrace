English version: [./README.md](./README.md)

# Использование и API

Эта страница — практический справочник по runtime API (`EmberTrace`) и типовым паттернам.

## Жизненный цикл

Одна сессия = один интервал между `Start()` и `Stop()`.

```csharp
Tracer.Start();

using (Tracer.Scope(Ids.App))
{
    // ...
}

var session = Tracer.Stop();
```

Примечания:
- вложенные `Scope` допускаются (получится call-tree)
- события собираются в **thread-local** буферы; тяжёлая обработка — после `Stop()`

## Scopes

### Sync: `Tracer.Scope(int id)`

`Scope` — `ref struct` (минимальный overhead). Его **нельзя** держать через `await`.

```csharp
using (Tracer.Scope(Ids.Cpu))
{
    CpuWork();
}
```

### Async: `Tracer.ScopeAsync(int id)`

```csharp
await using (Tracer.ScopeAsync(Ids.Io))
{
    await Task.Delay(10);
}
```

Async scope сопоставляется по собственной идентичности, а не по потоку, поэтому продолжение на другом потоке всё равно даёт корректную длительность. Вложенные scope — включая синхронные на worker-потоках — привязываются к нему как дети.

## Идентификаторы (TraceId)

EmberTrace работает с `int id`. Есть три удобных стратегии:

1) **Константы в коде** (самый явный вариант)

```csharp
static class Ids
{
    public const int App = 1000;
    public const int Io  = 2100;
}
```

2) **Стабильный id из строки** (когда не хочешь держать таблицу констант)

```csharp
var id = Tracer.Id("MySubsystem.Request");
using var _ = Tracer.Scope(id);
```

Не вызывай `Tracer.Id` на каждый скоуп: он хэширует строку и ищет имя в реестре при каждом вызове. Создай `TraceName`
один раз, в `static readonly` поле, и передавай его в `Tracer.Scope`:

```csharp
private static readonly TraceName Request = new("MySubsystem.Request");

using var _ = Tracer.Scope(Request);
```

Категорию можно хешировать так же:

```csharp
var ioCategory = Tracer.CategoryId("IO");
```

3) **Метаданные через `[assembly: TraceId(...)]`** — для красивых имён/категорий в отчёте и экспорте
(см. docs: generator).

## Настройки сессии

```csharp
Tracer.Start(new SessionOptions
{
    ChunkCapacity = 128 * 1024,
    OverflowPolicy = OverflowPolicy.DropNew
});
```

- `ChunkCapacity` — размер чанка событий в буфере потока
- `OverflowPolicy` — поведение при переполнении (`DropNew`, `DropOldest`, `StopSession`)
- `MaxTotalEvents` / `MaxTotalChunks` — лимиты на общий объём
- `MaxEventsPerSecond` — лимит событий в секунду на writer‑поток
- `SampleEveryNGlobal` / `SampleEveryNById` — sampling на всю сессию без глобальных lock
- `EnabledCategoryIds` / `DisabledCategoryIds` — фильтрация категорий

## Session API

После остановки сессии:

- `session.EventCount` — общее число событий
- `session.EnumerateEvents()` — сырые события (для своих инструментов)
- `session.EnumerateEventsSorted()` — стабильная сортировка по timestamp → thread → sequence
- `session.Process()` — агрегаты для отчётов/аналитики

```csharp
var processed = session.Process();
```

## Метаданные

Метаданные нужны, чтобы id превращались в человекочитаемые имена/категории.

```csharp
var meta = Tracer.CreateMetadata();
```

Если подключён `EmberTrace.Generator`, метаданные из `[assembly: TraceId(...)]` будут
**автоматически зарегистрированы** при старте модуля (см. [генератор](../../reference/source-generator/README.ru.md)).

Для dev‑сценариев можно включить runtime‑метаданные: `EnableRuntimeMetadata = true`.
В этом режиме `Tracer.Id("Name")` автоматически регистрирует имя с категорией `Default`.

## Измерение коротких операций

Скоуп имеет фиксированную цену. На машине, с которой сняты эти цифры (`ScopeBenchmarks`, короткий джоб на Linux),
открытие и закрытие одного занимает около 157 нс против около 61 нс на два вызова `Stopwatch.GetTimestamp()`, а
`Tracer.Id(...)` на каждом вызове добавляет ещё около 93 нс (используй `TraceName`, см. выше). Запусти
`ScopeBenchmarks`, чтобы получить свои числа.

Для операции в единицы микросекунд скоуп на каждый вызов измеряет в основном сам скоуп. Оберни пачку и раздели на её
размер:

```csharp
private static readonly TraceName ParseBatch = new("Parse.Batch");

using (Tracer.Scope(ParseBatch))
{
    for (var i = 0; i < items.Length; i++)
        Parse(items[i]);
}
```

Время на элемент — это `AverageMs / items.Length` этого скоупа. Как правило, скоуп короче 20 собственных накладных
расходов нужно измерять пачкой. `OverheadCalibration.Measure()` запускает отдельную сессию и возвращает цену на текущей
машине:

```csharp
var overhead = OverheadCalibration.Measure();

Console.WriteLine($"scope: {overhead.ScopeNs:F0} ns; wrap at least {overhead.MinBatchWorkNs(5):F0} ns of work per scope");
Console.WriteLine(TraceText.Write(session.Process(), includePercentiles: true, overhead: overhead));
```

`MinBatchWorkNs(5)` — работа, которую должен обернуть один скоуп, чтобы накладные расходы остались ниже 5%; `CorrectMs`
вычитает один скоуп из измеренной длительности; отчёт помечает скоупы, медиана которых меньше 20 накладных расходов,
как `overhead-dominated`. Запускай калибровку на той же машине и с теми же настройками, что и измеряемый код, при
остановленном трейсере.

## Практические рекомендации

- Инструментируй **крупные** участки горячего пути и внешние ожидания (IO/lock/await), а не каждую строку.
- Стабилизируй id: либо диапазоны констант, либо `Tracer.Id("...")` с фиксированными строками.
- Для `async` всегда используй `ScopeAsync`, иначе компилятор/рантайм ограничат сценарий.

См. также:
- [Flow и async](../../concepts/flows/README.ru.md)
- [Экспорт](../export/README.ru.md)
- [Анализ и отчёты](../analysis/README.ru.md)

## Скриншоты

![Пример кода: скриншот блока instrumentation (Scope + metadata)](../../assets/usage-instrumentation.png)
