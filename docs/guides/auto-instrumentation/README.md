Русская версия: [./README.ru.md](./README.ru.md)

# Auto-instrumentation with `[Trace]`

Declare a method `partial`, mark it `[Trace]`, and move the body into a `…Core` method. The generator
emits the wrapper, computes a stable id, and registers the name and the category.

## Before and after

Manual:

```csharp
public sealed class OrderService
{
    public async Task<Order> GetAsync(int id)
    {
        await using var _ = Tracer.ScopeAsync(Ids.OrderGet);
        return await _repository.LoadAsync(id).ConfigureAwait(false);
    }
}
```

Generated:

```csharp
using EmberTrace.Abstractions.Attributes;

public sealed partial class OrderService
{
    [Trace]
    public partial Task<Order> GetAsync(int id);

    private async Task<Order> GetAsyncCore(int id)
    {
        return await _repository.LoadAsync(id).ConfigureAwait(false);
    }
}
```

The id catalog disappears, the async scope can no longer be forgotten or left un-awaited, and `[Trace]`
is visible in the signature instead of buried in the body.

## The `…Core` convention

Every `[Trace] partial` method needs a method with the same name plus the `Core` suffix, the same
parameters, the same `ref`/`out`/`in` kinds, and the same return type. The containing type and every
type enclosing it must be `partial`.

On a generic method the core must reuse the **same type parameter names**: the match is made on rendered
signatures, so `T PickCore<T>(T value)` matches `T Pick<T>(T value)` while `T PickCore<TItem>(TItem value)`
does not.

## What is generated

Synchronous methods get a plain scope — `Tracer.Scope` already returns an inert scope when no session
runs, so no fast path is needed:

```csharp
public partial int Sum(int a, int b)
{
    using var __emberTraceScope = global::EmberTrace.Tracer.Scope(160062063);
    return SumCore(a, b);
}
```

Asynchronous methods get a wrapper that is deliberately **not** `async`. It branches on
`Tracer.IsRunning` and forwards straight to the core, so no state machine is built when nothing is
recording:

```csharp
public partial global::System.Threading.Tasks.Task<int> GetAsync(int id)
    => global::EmberTrace.Tracer.IsRunning ? GetAsync__EmberTraceTraced(id) : GetAsyncCore(id);

[global::System.Diagnostics.DebuggerNonUserCode]
private async global::System.Threading.Tasks.Task<int> GetAsync__EmberTraceTraced(int id)
{
    await using var __emberTraceScope = global::EmberTrace.Tracer.ScopeAsync(814080860);
    return await GetAsyncCore(id).ConfigureAwait(false);
}
```

`[DebuggerNonUserCode]` sits on the helper only: stepping into a traced method lands in your `…Core`
body, while the wrapper itself stays visible to Just My Code because that declaration *is* your method.

## Names, categories and ids

- The default name is `TypeName.MethodName` — `OrderService.GetAsync`.
- The category is resolved in this order: `[Trace(Category = "…")]`, `[TraceCategory]` on the method,
  `[TraceCategory]` on the containing type walking outward through nested types.
- The id is FNV-1a over the resolved name, the same hash `Tracer.Id(name)` uses, so
  `Tracer.Id("OrderService.GetAsync")` at runtime addresses exactly the scope the generator emitted.
- `[Trace("checkout")]` sets a name that never moves. `[Trace(Id = 4100)]` pins the id outright.

Traced methods join the same catalog as `[assembly: TraceId]` and annotated `const int` fields, so one
metadata provider is emitted per assembly and `ETG001` covers every id source at once.

## Overloads

Two traced overloads would otherwise share a name and therefore an id. When a type has more than one
traced method of the same name, every one of them is disambiguated by its parameter list:
`OrderService.GetAsync(int)`, `OrderService.GetAsync<T>(string, int)`.

The consequence is worth stating plainly: **adding a second overload renames — and so re-ids — the
first**. Use `[Trace("…")]` where an id must survive refactoring.

## The synchronous-throw asymmetry

If `…Core` is not itself `async` and throws before its first `await`, the untraced path propagates that
exception synchronously while the traced path returns a faulted task. This is how any hand-written fast
path behaves; it is documented rather than papered over.

## `ConfigureAwait(false)`

The generated helper awaits with `ConfigureAwait(false)`, so the continuation that closes the scope does
not capture a synchronization context. Event pairing is unaffected: `AsyncScope` carries
`scopeId`/`parentScopeId`, which is exactly why it exists.

## Unsupported shapes

| Shape | Diagnostic |
|-------|-----------|
| `[Trace]` on a method that is not a `partial` declaration awaiting an implementation | `ETG010` |
| No `…Core` member with a compatible signature | `ETG011` |
| `ref` returns, `ref`/`out`/`in` or `ref struct` parameters on async methods, `readonly` async members, `unsafe` methods, interface members, `IAsyncEnumerable<T>` | `ETG012` |
| The containing type, or an enclosing type, is not `partial` | `ETG013` |
| `[Trace]` on a class with no unambiguous interface | `ETG014` |
| An interface member of unsupported shape is forwarded by the decorator untraced | `ETG015` (info) |

`IAsyncEnumerable<T>` is deferred rather than rejected forever: a scope there would span the whole
enumeration rather than the call, which is a separate semantic decision.

## The DI decorator

`[Trace]` on a **class** generates `TracedX : IX` that wraps every traceable interface member, instead of
instrumenting method by method:

```csharp
using EmberTrace.Abstractions.Attributes;

[Trace]
[TraceCategory("Inventory")]
public partial class InventoryService : IInventoryService
{
    public int Available => 100;

    public int Reserve(int quantity) => quantity;

    public Task<int> ReserveAsync(int quantity) => Task.FromResult(quantity);
}
```

produces, in the same namespace and with the same accessibility as the class:

```csharp
public sealed class TracedInventoryService : global::Acme.IInventoryService
{
    private readonly global::Acme.IInventoryService _inner;

    public TracedInventoryService(global::Acme.IInventoryService inner)
    {
        _inner = inner;
    }

    public int Reserve(int quantity)
    {
        using var __emberTraceScope = global::EmberTrace.Tracer.Scope(1404967604);
        return _inner.Reserve(quantity);
    }

    public int Available { get => _inner.Available; }
}
```

Prefer the decorator when a whole service should be instrumented at its boundary and the service is
resolved through DI. Prefer per-method `[Trace]` when only some methods matter, or when internal calls
must be visible — **the decorator only sees calls that go through the interface**, so a method the
service calls on itself is invisible to it. That is the one thing the per-method style does better.

Rules:

- The interface is the single directly-implemented interface, or the one named by
  `[Trace(Interface = typeof(IX))]`. Anything else is `ETG014`.
- Scope names use the **class** name — `InventoryService.Reserve` — not the interface name, so a class
  can be instrumented either way without producing two different names for the same method.
- Properties, events and indexers are forwarded without a scope. Members of unsupported shape are
  forwarded untraced and reported as `ETG015` (info).
- A generic interface produces a generic decorator, constraints included.
- `AddTracedInventoryService(this IServiceCollection services, ServiceLifetime lifetime)` is emitted
  **only when** `Microsoft.Extensions.DependencyInjection.IServiceCollection` is present in the
  compilation — a generator cannot add a package reference on your behalf. It registers the decorator
  under the interface and builds the concrete service with `ActivatorUtilities.CreateInstance`, so
  constructor injection keeps working and no untraced instance ends up in the container.

## Migrating existing code — `ETA004`

`ETA004` (info) flags a non-partial method whose body opens with a manual `Tracer.Scope` /
`Tracer.ScopeAsync`, which is exactly a method that should become a `[Trace] partial`. The fix renames
it to `…Core`, makes it `private`, drops the manual `using`, and inserts the `[Trace] partial`
declaration above it. Fix All migrates a whole service layer in one action.

The fix deliberately does **not** add the `using EmberTrace.Abstractions.Attributes;` directive or mark
the containing type `partial`; both are then reported by `ETG010`/`ETG013` with their own compiler
fixes, and attempting all three in one code action makes Fix All fragile.

## Overhead

Measured with `benchmarks/EmberTrace.Benchmarks`, 10,000 operations per benchmark invocation, no session
running:

| Benchmark | Mean | Allocated |
|-----------|------|-----------|
| `Trace_Sync_SessionStopped` | 5.29 us | — |
| `Trace_Async_SessionStopped` | 11.72 us | 72 B |

The 72 bytes are the benchmark method's own state machine, not a per-call allocation: the async wrapper
never enters an `async` method while the session is stopped.

See also:
- [Generator and TraceId (metadata)](../../reference/source-generator/README.md)
- [Roslyn analyzers](../../reference/roslyn-analyzers/README.md)
- [Usage and API](../usage/README.md)
