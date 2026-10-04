English version: [./README.md](./README.md)

# SessionOptions

`SessionOptions` задают поведение записи и защиту от переполнения.

## Основные

- `ChunkCapacity` — событий в чанке (по умолчанию `4_096`, минимум `1_024`). Событие занимает
  32 байта, поэтому чанк — это `ChunkCapacity × 32` байт: 128 КБ по умолчанию. Каждый пишущий
  поток держит как минимум один чанк.
- `OverflowPolicy` — политика при переполнении:
  - `DropNew` — отбрасывать новые события
  - `DropOldest` — перезаписывать самые старые чанки
  - `StopSession` — остановить сессию
- `MaxTotalEvents` — лимит событий в сессии (0 = без лимита)
- `MaxTotalChunks` — лимит чанков (0 = без лимита). При `DropOldest` лимит действует на
  завершённые чанки: чанки завершившихся потоков переиспользуются, а живому потоку буфер
  выдаётся всегда, поэтому сессия может держать до одного чанка на живой поток сверх лимита.
- `MaxRetentionWindow` — хранить только последние N времени по стенным часам
  (по умолчанию `TimeSpan.Zero` = выкл). Требует `OverflowPolicy.DropOldest` и не может
  превышать сутки, иначе `Tracer.Start` бросает исключение. Применяется при ротации чанка и
  при снятии снапшота, но не к чанкам, которыми владеет писатель.
  См. [Flight recorder](../../guides/flight-recorder/README.ru.md).

## Фильтрация и sampling

- `EnabledCategoryIds` — список разрешённых категорий (whitelist)
- `DisabledCategoryIds` — список запрещённых категорий (blacklist)
- `SampleEveryNGlobal` — пропускать N‑1 событий из N глобально (0/1 = выкл)
- `SampleEveryNById` — словарь `{ id -> everyN }` для точечного sampling
- `MaxEventsPerSecond` — лимит событий в секунду на writer (0 = без лимита)

Счётчики sampling общие для всей сессии, а не для потока: writer‑потоки резервируют тикеты
из одной глобальной последовательности блоками по 127, поэтому доля сохранённых событий
остаётся `1/N` при любом числе потоков, а короткоживущие потоки больше не сохраняют своё
первое событие безусловно. Размер блока взаимно прост с любым практическим `everyN`, за счёт
чего границы блоков не совпадают с периодом sampling.

`MaxEventsPerSecond`, в отличие от sampling, действует на каждый writer‑поток отдельно:
фактический потолок процесса — `MaxEventsPerSecond` x число пишущих потоков.

## Метаданные

- `EnableRuntimeMetadata` — подмешивать имена, записанные через `Tracer.Id`, в метаданные этой
  сессии (по умолчанию `false`, в любой конфигурации сборки). Действует только на саму сессию:
  глобальный реестр провайдеров не меняется. Значение по умолчанию можно переключить без кода —
  через host configuration switch `EmberTrace.EnableRuntimeMetadata`:

```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="EmberTrace.EnableRuntimeMetadata" Value="true" />
</ItemGroup>
```

## Runtime-счётчики

| Опция | По умолчанию | Смысл |
|-------|--------------|-------|
| `RuntimeCounters` | `RuntimeCounters.None` | Какие группы метрик рантайма снимать на отдельную дорожку. См. [Runtime-счётчики](../../guides/runtime-counters/README.ru.md). |
| `RuntimeCounterInterval` | `50 мс` | Период сэмплирования. Зажимается в [1 мс, 60 с]. |

Runtime-счётчики обходят `EnabledCategoryIds` / `DisabledCategoryIds`.

## Callbacks

- `OnOverflow` — вызывается один раз при первом overflow
- `OnMismatchedEnd` — вызывается при обнаружении mismatched end в `Analyze/Process`

`OnOverflow` никогда не выполняется на потоке, записавшем переполнившее событие: он ставится в
пул потоков, поэтому внутри него безопасно трассировать, брать блокировки и вызывать `Stop()`.
Взамен доставка асинхронна и не гарантирует своевременности — обработчик может быть ещё не вызван
к моменту возврата из `Stop()` и может не выполниться вовсе, если процесс завершится раньше.
Когда нужен однозначный ответ после окончания сессии, используйте `TraceSession.WasOverflow`.
Исключения обработчика подавляются.

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
