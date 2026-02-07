Русская версия: [./README.ru.md](./README.ru.md)

# SessionOptions

`SessionOptions` define recording behavior and overflow protection.

## Core

- `ChunkCapacity` - event chunk size (default `16_384`)
- `OverflowPolicy` - overflow policy:
  - `DropNew` - drop new events
  - `DropOldest` - overwrite oldest chunks
  - `StopSession` - stop the session
- `MaxTotalEvents` - event limit per session (0 = unlimited)
- `MaxTotalChunks` - chunk limit (0 = unlimited)

## Filtering and sampling

- `EnabledCategoryIds` - list of allowed categories (whitelist)
- `DisabledCategoryIds` - list of blocked categories (blacklist)
- `SampleEveryNGlobal` - keep 1 event out of N globally (0/1 = off)
- `SampleEveryNById` - dictionary `{ id -> everyN }` for targeted sampling
- `MaxEventsPerSecond` - events-per-second cap per writer (0 = unlimited)

## Metadata

- `EnableRuntimeMetadata` - register runtime metadata (see `Tracer.Id`)

## Callbacks

- `OnOverflow` - called once on first overflow
- `OnMismatchedEnd` - called when mismatched end is detected in `Analyze/Process`

## Example

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
