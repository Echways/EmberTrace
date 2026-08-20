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

Sampling counters are shared by the whole session, not by thread: writer threads reserve
tickets from one global sequence in blocks of 127, so the kept share stays `1/N` no matter
how many threads produce events, and short-lived threads no longer keep their first event
unconditionally. The block size is coprime with any practical `everyN`, which keeps the
block boundaries from lining up with the sampling period.

`MaxEventsPerSecond`, unlike sampling, is enforced per writer thread: the effective ceiling
for the process is `MaxEventsPerSecond` x number of threads that write events.

## Metadata

- `EnableRuntimeMetadata` - mix the names recorded by `Tracer.Id` into this session's metadata
  (default `false`, in every build configuration). Scoped to the session: it never registers a
  provider globally. The default can be flipped without code through the host configuration
  switch `EmberTrace.EnableRuntimeMetadata`:

```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="EmberTrace.EnableRuntimeMetadata" Value="true" />
</ItemGroup>
```

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
