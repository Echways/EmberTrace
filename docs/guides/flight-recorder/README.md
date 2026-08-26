Русская версия: [./README.ru.md](./README.ru.md)

# Flight recorder

A flight recorder keeps a bounded window of the recent past in memory and lets you dump it
*without stopping the session*. It turns EmberTrace from a benchmark tool into something you
can leave running in a service: when a request times out, a p99 spikes, or an exception
escapes, you dump the last few seconds of the trace and keep recording.

## Turning it on

```csharp
Tracer.Start(new SessionOptions
{
    OverflowPolicy = OverflowPolicy.DropOldest,
    MaxRetentionWindow = TimeSpan.FromSeconds(10),
    MaxTotalChunks = 256
});
```

- `OverflowPolicy.DropOldest` is required. `MaxRetentionWindow` throws `ArgumentException`
  with any other policy, because dropping *new* events is the opposite of a flight recorder.
- `MaxRetentionWindow` bounds the buffer by wall-clock age. `MaxTotalChunks` bounds it by
  memory. Set both: the window is what you want, the chunk cap is what protects the process
  when the event rate spikes.
- The window is enforced when a writer thread rotates to a new chunk, and again when a
  snapshot is taken. A chunk still owned by an idle thread is never trimmed, so a thread that
  stops producing events keeps its last partial chunk.

## Taking a snapshot

```csharp
var snapshot = Tracer.Snapshot();
var recent = Tracer.Snapshot(TimeSpan.FromSeconds(5));
```

`Snapshot()` copies everything the buffer currently holds. `Snapshot(window)` copies only the
events at or after `now - window`, which is what you want when the buffer holds 60 seconds but
the incident is 5 seconds wide.

The result is an ordinary `TraceSession`. Everything that works on a stopped session works on
it — `Process()`, `TraceText.Write(...)`, `TraceExport.*`, `TraceFormat.Write(...)`:

```csharp
try
{
    await HandleRequestAsync(request, timeout);
}
catch (TimeoutException)
{
    TraceFormat.Write(Tracer.Snapshot(TimeSpan.FromSeconds(10)), $"/var/log/trace-{DateTime.UtcNow:O}.ember");
}
```

## What a snapshot guarantees

- **The session keeps running.** `Tracer.IsRunning` stays `true`, writer threads are never
  paused, and no event is lost because a snapshot was taken.
- **It does not drain the buffer.** Two consecutive snapshots overlap. The buffer is only
  emptied by the retention window and the overflow policy.
- **Events are never torn.** A snapshot copies a consistent cut of every chunk. Chunks that
  would have been recycled while the copy runs are parked and released afterwards.
- **`EndTimestamp` is the cut.** Every event in the snapshot satisfies
  `Timestamp <= EndTimestamp`.
- **`IsSnapshot` is `true`**, and it survives a `TraceFormat` round trip.

## What a snapshot does not guarantee

- **It is not a global instant.** Threads are copied one after another, so the cut is not a
  synchronized barrier across threads. For a trace this is the right trade: correctness of
  each event, not simultaneity of all of them.
- **Scopes open at the cut appear unclosed.** `ScopeReader` synthesizes a close at
  `EndTimestamp` for them, exactly as it does for a session that stopped mid-scope, and marks
  the step `IsSynthetic`. Consumers that skip synthetic steps skip these too: the Chrome Trace
  exporter emits no complete span for a scope that was still open at the cut. Widen nothing —
  this is the same behaviour a session stopped mid-scope has always had.
- **A windowed snapshot can cut a scope's `Begin` away.** The orphan `End` is counted in
  `ScopeReader.UnmatchedEndCount` and dropped. Widen the window if that matters.

## Cost

The recording hot path is unchanged: nothing was added to the per-event write. A snapshot
costs one `Array.Copy` per chunk plus a short lock acquisition to capture the chunk list.
While a snapshot is in flight the buffer may hold up to twice its steady-state chunks, because
recycling is deferred until the copy finishes; the excess is returned to the pool immediately
afterwards.

`TraceSession.DroppedEvents` and `DroppedChunks` include events retired by the retention
window. Ageing out is not an overflow: it does not set `WasOverflow` and does not fire
`OnOverflow` on its own.

## Full example

See [`samples/EmberTrace.FlightRecorder`](../../../samples/EmberTrace.FlightRecorder).
