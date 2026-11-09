# Устранение неполадок

[English](README.md) · [Русский](README.ru.md) · [Документация](../README.ru.md)

Ниже — типовые симптомы, причины и короткие фиксы.

## В сессии нет событий

**Симптом:** `EventCount == 0`, отчёт пустой.

**Проверьте:**
- `Tracer.Start()` вызван **до** первых `Scope/Flow`
- `Tracer.Stop()` реально был вызван (и сессия не «упала» по исключению раньше)
- id не равен `0` (избегайте `id == 0` — это частый источник путаницы)

## Ошибка/предупреждение про `await` и `Scope`

**Причина:** `Scope` — `ref struct`, его нельзя переносить через `await`.

**Решение:** используйте `ScopeAsync`.

```csharp
await using (Tracer.ScopeAsync(Ids.Io))
{
    await Task.Delay(10);
}
```

`ScopeAsync` несёт собственный scope id в `Begin`/`End`, поэтому переезд на другой поток через `await` не ломает ни сопоставление, ни вложенность.

## Непарные begin/end вокруг `await`

**Причина:** `AsyncScope` не освобождается в том же async-контексте — сохранён в поле, освобождён другой задачей или создан без `await using`.

**Решение:** всегда создавайте его в конструкции `await using` (остальное подсветит анализатор `ETA002`).

## В отчёте нет имён (только числа)

**Причина:** нет метаданных.

**Решение:**
- добавьте `[assembly: TraceId(...)]`
- подключите `EmberTrace.Abstractions` + `EmberTrace.Generator`
- пересобери проект (source generator генерирует и регистрирует провайдер при компиляции)

![Нет имён в отчёте: отсутствуют метаданные TraceId](../assets/troubleshooting-common.png)

## Переполнение буфера / «пропали события»

**Причина:** буферы потока переполнены, сработала политика overflow.

**Решение:**
- увеличьте `SessionOptions.ChunkCapacity`
- пересмотри частоту/гранулярность instrumentation
- при необходимости поменяй `OverflowPolicy` (`DropNew`, `DropOldest`, `StopSession`)
- проверьте лимиты `MaxTotalEvents`, `MaxTotalChunks`, `MaxEventsPerSecond`

## Медиана и среднее расходятся

**Причина:** длительности scope'а образуют два кластера (путь с кэшем и без, блокировка, за которую иногда борются), поэтому
медиана следует за кластером с большим числом замеров и сильно сдвигается при небольшом изменении их соотношения.
Отчёт помечает такой scope `*` и `median unreliable`; `row.Shape.IsBimodal` равен `true`.

**Решение:**
- смотрите `row.Shape` (`P25Ms`, `P75Ms`, `MeanMs`) и `row.Warmup()` вместо одного числа
- раздели scope по ветвям (`Tracer.Scope` на каждый путь), чтобы в каждом был один кластер
- при сравнении наборов запусков бимодальный scope получает `Inconclusive` при `Statistic.Median`: используйте
  `Statistic.Mean` или раздели scope

## Запуски одного и того же кода расходятся / смешанные governor процессора

**Причина:** состояние машины менялось между запусками (governor `powersave` против `performance`, turbo, другой рантайм
или настройка `DOTNET_*`, affinity или лимиты cgroup).

**Решение:**
- сравните `session.Environment` через `EnvironmentCompatibility.Compare` или смотрите `RunSetComparison.Mismatches`
- сопоставьте `session.EnvironmentAtStop` с `session.Environment`: governor, изменившийся за время прогона, виден там
- включите `ComparisonOptions.RequireCompatibleEnvironment`, чтобы любое блокирующее различие делало все вердикты
  `Inconclusive`, и перезапусти на одной фиксированной конфигурации

## Flow «обрывается»

**Причина:** не вызван `FlowEnd(...)` / `handle.End()`.

**Решение:** закрывайте flow в `finally` или через явный жизненный цикл.

## Есть только числовые id в dev‑сборке

**Причина:** нет метаданных, generator не подключён.

**Решение:** включите `EnableRuntimeMetadata = true` или добавьте `[assembly: TraceId(...)]`.

## См. также

- [Использование и API](../guides/usage/README.ru.md)
- [Flow и async](../concepts/flows/README.ru.md)
