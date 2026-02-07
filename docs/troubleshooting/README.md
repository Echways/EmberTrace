Русская версия: [./README.ru.md](./README.ru.md)

# Troubleshooting

Below are common symptoms, causes, and short fixes.

## No events in the session

**Symptom:** `EventCount == 0`, report is empty.

**Check:**
- `Tracer.Start()` is called **before** the first `Scope/Flow`
- `Tracer.Stop()` was actually called (and the session did not terminate earlier due to an exception)
- id is not `0` (avoid `id == 0` - this is a frequent source of confusion)

## Error/warning about `await` and `Scope`

**Cause:** `Scope` is a `ref struct`; it cannot be carried through `await`.

**Fix:** use `ScopeAsync`.

```csharp
await using (Tracer.ScopeAsync(Ids.Io))
{
    await Task.Delay(10);
}
```

## No names in the report (only numbers)

**Cause:** metadata is missing.

**Fix:**
- add `[assembly: TraceId(...)]`
- connect `EmberTrace.Abstractions` + `EmberTrace.Generator`
- rebuild the project (the source generator creates and registers the provider at compile time)

## Buffer overflow / "missing events"

**Cause:** thread buffers overflowed and the overflow policy triggered.

**Fix:**
- increase `SessionOptions.ChunkCapacity`
- review instrumentation rate/granularity
- change `OverflowPolicy` if needed (`DropNew`, `DropOldest`, `StopSession`)
- check limits `MaxTotalEvents`, `MaxTotalChunks`, `MaxEventsPerSecond`

## Flow is "broken"

**Cause:** `FlowEnd(...)` / `handle.End()` was not called.

**Fix:** close flow in `finally` or via explicit lifecycle.

## Only numeric IDs in a dev build

**Cause:** metadata is missing; generator is not connected.

**Fix:** enable `EnableRuntimeMetadata = true` or add `[assembly: TraceId(...)]`.

See also:
- [Usage and API](../guides/usage/README.md)
- [Flow and async](../concepts/flows/README.md)

## Screenshots

![Нет имён в отчёте: отсутствуют метаданные TraceId](../assets/troubleshooting-common.png)
