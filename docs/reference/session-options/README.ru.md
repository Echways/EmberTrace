English version: [./README.md](./README.md)

# SessionOptions

`SessionOptions` задают поведение записи и защиту от переполнения.

## Основные

- `ChunkCapacity` — размер чанка событий (по умолчанию `16_384`)
- `OverflowPolicy` — политика при переполнении:
  - `DropNew` — отбрасывать новые события
  - `DropOldest` — перезаписывать самые старые чанки
  - `StopSession` — остановить сессию
- `MaxTotalEvents` — лимит событий в сессии (0 = без лимита)
- `MaxTotalChunks` — лимит чанков (0 = без лимита)

## Фильтрация и sampling

- `EnabledCategoryIds` — список разрешённых категорий (whitelist)
- `DisabledCategoryIds` — список запрещённых категорий (blacklist)
- `SampleEveryNGlobal` — пропускать N‑1 событий из N глобально (0/1 = выкл)
- `SampleEveryNById` — словарь `{ id -> everyN }` для точечного sampling
- `MaxEventsPerSecond` — лимит событий в секунду на writer (0 = без лимита)

## Метаданные

- `EnableRuntimeMetadata` — регистрировать runtime‑метаданные (см. `Tracer.Id`)

## Callbacks

- `OnOverflow` — вызывается один раз при первом overflow
- `OnMismatchedEnd` — вызывается при обнаружении mismatched end в `Analyze/Process`

## Пример

```csharp
Tracer.Start(new SessionOptions
{
    ChunkCapacity = 64 * 1024,
    OverflowPolicy = OverflowPolicy.DropOldest,
    MaxTotalEvents = 5_000_000,
    EnabledCategoryIds = new[] { Tracer.CategoryId("IO"), Tracer.CategoryId("CPU") },
    SampleEveryNGlobal = 10,
    MaxEventsPerSecond = 200_000
});
```
