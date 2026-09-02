Русская версия: [./tracer.ru.md](./tracer.ru.md)

# Tracer

`Tracer` is the public entry point for trace recording (scopes, flows) and session control.

> Namespace: `EmberTrace`  
> Source: `src/EmberTrace/Api/Tracer.cs`

---

## Quick example

```csharp
using EmberTrace;

var parseId = Tracer.Id("parse");
var flowStepId = Tracer.Id("flow.step");

Tracer.Start();

using (Tracer.Scope(parseId))
{
    var flowId = Tracer.FlowStartNew(flowStepId);
    Tracer.FlowStep(flowStepId, flowId);
    Tracer.FlowEnd(flowStepId, flowId);
}

var session = Tracer.Stop();
// next: export / report - see the Export and Analysis guides
```

---

## Session control

### `bool Tracer.IsRunning`
`true` if the profiler is active and events are being written.

### `void Tracer.Start(SessionOptions? options = null)`
Starts event recording.

- `options = null` -> default values are used (see `SessionOptions`).

### `TraceSession Tracer.Stop()`
Stops recording and returns `TraceSession` with collected events.

### `TraceSession Tracer.Snapshot()`
### `TraceSession Tracer.Snapshot(TimeSpan window)`
Copies the current buffer into a `TraceSession` **without stopping the session**. The overload
keeps only events newer than `window`. Returns an empty session when nothing is running, and
throws `ArgumentOutOfRangeException` for a negative window. The returned session has
`IsSnapshot == true`. See [Flight recorder](../../guides/flight-recorder/README.md).

---

## Scopes

### `Scope Tracer.Scope(int id)`
Opens a scope on the current thread and returns `Scope` (stack-only `ref struct`).

- Use **only** in synchronous code (a scope cannot be carried across `await`).
- `Scope` calls `Profiler.End(id)` in `Dispose()`.

Example:

```csharp
using (Tracer.Scope(Tracer.Id("load")))
{
    Load();
}
```

### `AsyncScope Tracer.ScopeAsync(int id)`
Async-friendly scope implementing `IAsyncDisposable`.

- Construction writes `Begin` only if `Tracer.IsRunning == true`.
- Every instance gets a unique async scope id. Both `Begin` and `End` carry it, so the scope is matched by identity, not by thread: the continuation may resume on any thread and the duration stays correct.
- The id flows through `ExecutionContext`, so scopes opened inside it - including synchronous `Scope` on other threads - are recorded as its children.
- `DisposeAsync()` writes `End` and restores the enclosing async scope.
- Chrome trace export writes async scopes as `ph: "b"/"e"` pairs with `id`, so each of them gets its own async track.

Example:

```csharp
await using var _ = Tracer.ScopeAsync(Tracer.Id("io"));
await DoIoAsync();
```

> Why two APIs: `Scope` is a `ref struct` (faster/allocation-free), but incompatible with `await`.
> Use `ScopeAsync` for async code.

---

## Flows

Flows are a linked set of events (start/step/end) that can be propagated across async/threads.

### `long Tracer.NewFlowId()`
Generates a new `flowId` (unique within the process).

### `long Tracer.FlowStartNew(int id)`
Creates a new `flowId`, writes `FlowStart`, and returns `flowId`.

### `FlowScope Tracer.Flow(int id)`
Convenient scope variant: creates a flow and ends it in `Dispose()`.

### `void Tracer.FlowStart(int id, long flowId)`
Writes `FlowStart` for the specified `flowId`.

### `void Tracer.FlowStep(int id, long flowId)`
Writes `FlowStep` for the specified `flowId`.

### `void Tracer.FlowEnd(int id, long flowId)`
Writes `FlowEnd` for the specified `flowId`.

### `long Tracer.FlowFromActivityCurrent(int id)`
If `Activity.Current` exists, creates a flow using its trace id.

### `FlowHandle Tracer.FlowStartNewHandle(int id)`
Convenient wrapper over flow:

- creates a flow and returns `FlowHandle` with `Step()` / `End()` methods
- `FlowHandle.End()` is idempotent (repeated calls are safe)

### `void Tracer.FlowStep(FlowHandle handle)`
Calls `handle.Step()`.

### `void Tracer.FlowEnd(FlowHandle handle)`
Calls `handle.End()`.

---

## Metadata

### `ITraceMetadataProvider Tracer.CreateMetadata()`
Returns the metadata provider (names, categories, etc.) of the current or most recent
`Tracer` session, falling back to the globally registered providers before the first `Start`.

`Tracer.Id("Name")` always records the name with category `Default`. Those runtime names are
mixed into a session's metadata only when that session was started with
`SessionOptions.EnableRuntimeMetadata = true`, and only for that session — starting a session
never mutates the global provider registry.

Every completed session also exposes its own provider as `TraceSession.Metadata`, which the
exporters and `TraceText.Write` use by default when no `meta` argument is passed.

---

## ID Helpers

### `int Tracer.Id(string name)`
Stable string-based `int` identifier computed as a 31-bit FNV-1a hash.

- Deterministic: same string → same `id`.
- **Collision risk**: the hash space holds ~2.1 billion values. By the birthday paradox, the probability of a collision is ~1% at about **6 500** unique names and ~50% at about **54 000** — a realistic threshold in a large monorepo. A collision silently merges two spans into one, which corrupts aggregates instead of emptying them.
- Intended for a **bounded set of static names**. Do not build names per request (`Tracer.Id($"req:{userId}")`): every distinct name is retained for the lifetime of the process; see `Tracer.MaxTrackedNames`.
- Collision behaviour is controlled by `Tracer.IdCollisionMode` (see below).
- For projects with many unique trace names, prefer the source generator or `[TraceId]` attribute — they guarantee collision-free identifiers at compile time.

### `TracerIdCollisionMode Tracer.IdCollisionMode`
Controls what happens when two distinct names hash to the same value.

| Value | Behaviour | Default |
|-------|-----------|---------|
| `Throw` | Throws `InvalidOperationException` | — |
| `Warn` | Invokes `Tracer.OnIdCollision`, or falls back to `Trace.TraceWarning` when no handler is set | **Yes** |
| `Ignore` | Silently keeps the first mapping; correctness not guaranteed | — |

The mode only controls how a detected collision is reported; names are tracked the same way
in every mode and in every build configuration. The starting value can be changed without code
through the runtime host configuration property `EmberTrace.IdCollisionMode`:

```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="EmberTrace.IdCollisionMode" Value="Throw" />
</ItemGroup>
```

> **Recommendation for CI**: set `Tracer.IdCollisionMode = TracerIdCollisionMode.Throw` early in your test entry point. This surfaces collisions immediately rather than letting them corrupt traces silently.

### `Action<TracerIdCollision>? Tracer.OnIdCollision`
Called on every detected collision, before the mode is applied, with the id and both names.
Route it to your own logger or metrics — `Trace.TraceWarning` is only the fallback for `Warn`
when no handler is set.

```csharp
Tracer.OnIdCollision = c => logger.LogError("EmberTrace id collision: {Collision}", c);
```

### `int Tracer.MaxTrackedNames`
Upper bound on how many `name → id` pairs the tracer retains for collision detection and
runtime metadata. Default `16 384`; `0` disables the limit. Once the limit is reached, new
names are no longer registered: they still get an id, but stop being covered by collision
detection and stop appearing by name in traces. This caps the memory a dynamic-name misuse
can leak.

### `int Tracer.CategoryId(string category)`
Stable `int` identifier for categories (used in filters).

---

## Instant / Counter

### `void Tracer.Instant(int id)`
Writes an instant event.

### `void Tracer.Counter(int id, long value)`
Writes a counter value.

---

## Screenshots

![Tracer API in Perfetto](../../assets/api-tracer-perfetto.png)
