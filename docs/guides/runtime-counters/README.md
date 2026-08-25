Русская версия: [./README.ru.md](./README.ru.md)

# Runtime counters

EmberTrace can sample .NET runtime health onto the same timeline as your scopes, so a slow scope and the Gen2
collection that caused it line up visually.

## Enabling

```csharp
Tracer.Start(new SessionOptions
{
    RuntimeCounters = RuntimeCounters.All,
    RuntimeCounterInterval = TimeSpan.FromMilliseconds(50)
});
```

Counters are off by default. Groups combine as flags:

| Flag | Emits |
|------|-------|
| `Gc` | Gen0 / Gen1 / Gen2 collection counts, as per-interval deltas |
| `Memory` | Heap bytes (absolute) and allocated bytes (per-interval delta) |
| `ThreadPool` | Thread count and queue length (absolute), completed items (delta) |
| `Exceptions` | First-chance exception count, as a per-interval delta |
| `GcPauses` | Approximate GC pause spans |
| `All` | Everything above |

## Deltas versus gauges

Cumulative runtime counters are emitted as deltas since the previous sample. A monotonically rising line on a
counter track hides exactly the spikes you are looking for; a delta shows them. Gauges - heap size, thread count,
queue length - are emitted absolute. The first sample emits zero for every delta, because it establishes the
baseline. A counter that goes backwards is clamped to zero rather than producing a negative rate.

## Where the counters appear

The sampler runs on a dedicated background thread named `EmberTrace.Runtime`, so its events land on their own
track and render as a separate row in Perfetto, below your scope tracks.

## Reserved ids

Runtime counters use negative trace ids, listed in `RuntimeCounterIds`. `Tracer.Id` masks its hash with
`0x7fffffff` and never returns 0, so a user id is always positive and can never collide with this range. Use
`RuntimeCounterIds.IsReserved(id)` to filter EmberTrace's own events out of a report.

```csharp
var session = Tracer.Stop();

foreach (var e in session.EnumerateEventsSorted())
    if (!RuntimeCounterIds.IsReserved(e.Id))
        Handle(e);
```

## Category filters do not apply

Runtime counters bypass `EnabledCategoryIds` / `DisabledCategoryIds`. They are controlled by `RuntimeCounters`
alone, so a category allowlist cannot silently discard counters you explicitly asked for.

## GC pause accuracy

`GCMemoryInfo` reports a pause duration but no absolute start time, so a pause span is drawn as ending at the
sample that observed it. Placement is therefore accurate only to the sampling interval; the duration is exact.
Tighten `RuntimeCounterInterval` for better placement, at the cost of more samples.

## Cost

The sampler is one background thread at `BelowNormal` priority doing a handful of property reads per interval.
It writes through the same lock-free path as any other event and adds nothing to your application's hot path.
At the 50 ms default it produces roughly 200 events per second.
