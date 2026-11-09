# Session options

[English](README.md) · [Русский](README.ru.md) · [Documentation](../../README.md)

`SessionOptions` define how a session records and how it protects itself from overflow. Pass them to `Tracer.Start` or to `TracingSession.Start`.

```csharp
Tracer.Start(new SessionOptions
{
    ChunkCapacity = 64 * 1024,
    OverflowPolicy = OverflowPolicy.DropOldest,
    MaxTotalEvents = 5_000_000,
    EnabledCategoryIds = [Tracer.CategoryId("IO"), Tracer.CategoryId("CPU")],
    SampleEveryNGlobal = 10,
    MaxEventsPerSecond = 200_000
});
```

## Buffers and limits

| Option | Default | Meaning |
| ------ | ------- | ------- |
| `ChunkCapacity` | `4_096` | Events per chunk, at least `1_024`. An event is 32 bytes, so a chunk is `ChunkCapacity × 32` bytes: 128 KB by default. Every thread that writes holds at least one chunk. |
| `OverflowPolicy` | `DropNew` | What happens when a limit is reached: `DropNew` drops new events, `DropOldest` overwrites the oldest chunks, `StopSession` stops the session. |
| `MaxTotalEvents` | `0` | Event limit per session. `0` means unlimited. |
| `MaxTotalChunks` | `0` | Chunk limit. `0` means unlimited. With `DropOldest` it applies to finished chunks: chunks of exited threads are reclaimed and a live thread is never refused a buffer, so the session may hold up to one chunk per live thread above the limit. |
| `MaxRetentionWindow` | `TimeSpan.Zero` | Keep only the last N of wall-clock time; zero is off. Requires `OverflowPolicy.DropOldest` and must not exceed one day, otherwise `Tracer.Start` throws. Enforced on chunk rotation and on snapshot, never on chunks a writer still owns. See [Flight recorder](../../guides/flight-recorder/README.md). |

## Filtering and sampling

| Option | Default | Meaning |
| ------ | ------- | ------- |
| `EnabledCategoryIds` | none | Allowed categories, a whitelist. |
| `DisabledCategoryIds` | none | Blocked categories, a blacklist. |
| `SampleEveryNGlobal` | `0` | Keep one event out of N across the session. `0` and `1` are off. |
| `SampleEveryNById` | none | A dictionary `{ id -> everyN }` for targeted sampling. |
| `MaxEventsPerSecond` | `0` | Events-per-second cap per writer thread. `0` means unlimited. |

Sampling counters are shared by the whole session, not by thread. Writer threads reserve tickets from one global sequence in blocks of 127, so the kept share stays `1/N` no matter how many threads produce events, and short-lived threads do not keep their first event unconditionally. The block size is coprime with any practical `everyN`, which keeps the block boundaries from lining up with the sampling period.

`MaxEventsPerSecond`, unlike sampling, is enforced per writer thread: the effective ceiling for the process is `MaxEventsPerSecond` × the number of threads that write events.

## Metadata

| Option | Default | Meaning |
| ------ | ------- | ------- |
| `EnableRuntimeMetadata` | `false` | Mix the names recorded by `Tracer.Id` into this session's metadata. The default is the same in every build configuration. The option is scoped to the session and never registers a provider globally. |

The default can be flipped without code through the host configuration switch `EmberTrace.EnableRuntimeMetadata`:

```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="EmberTrace.EnableRuntimeMetadata" Value="true" />
</ItemGroup>
```

## Runtime counters

| Option | Default | Meaning |
| ------ | ------- | ------- |
| `RuntimeCounters` | `RuntimeCounters.None` | Which runtime metric groups to sample onto a dedicated track. See [Runtime counters](../../guides/runtime-counters/README.md). |
| `RuntimeCounterInterval` | `50 ms` | Sampling period, clamped to [1 ms, 60 s]. |

Runtime counters bypass `EnabledCategoryIds` and `DisabledCategoryIds`.

## Environment and tags

| Option | Default | Meaning |
| ------ | ------- | ------- |
| `CaptureEnvironment` | `true` | Record the machine and runtime settings at start and at stop. They are stored in `.ember` files and used to judge whether two recordings are comparable. |
| `Tags` | empty | String pairs stored with the session, for example `arm` and `round` for [run comparison](../../guides/analysis/README.md#comparing-sets-of-runs). |

## Callbacks

| Option | Called |
| ------ | ------ |
| `OnOverflow` | Once, on the first overflow. |
| `OnMismatchedEnd` | When `Analyze` or `Process` detects a mismatched end. |

`OnOverflow` never runs on the thread that recorded the overflowing event: it is queued to the thread pool, so it is safe to trace, lock, or call `Stop()` from inside it. In exchange, delivery is asynchronous and not guaranteed to be timely. The handler may still be pending when `Stop()` returns, and it may not run at all if the process exits first. Use `TraceSession.WasOverflow` when you need a definitive answer after the session ends. Exceptions thrown by the handler are swallowed.
