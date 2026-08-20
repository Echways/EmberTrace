Русская версия: [./README.ru.md](./README.ru.md)

# Generator and TraceId (metadata)

`EmberTrace` works with `int id`. To get readable **names** and **categories** in exports/reports,
use assembly attributes and the source generator.

## What the generator does

`EmberTrace.Generator`:

1) Scans the project for `[assembly: TraceId(id, name, category)]` and for `const int` fields
   marked with `[TraceName]` / `[TraceCategory]`
2) Generates a metadata provider (`ITraceMetadataProvider`)
3) **Automatically registers** it via `ModuleInitializer`, so `Tracer.CreateMetadata()`
   starts returning names/categories without manual initialization.
4) Optionally generates `TraceIds.g.cs` with `const int` values for each TraceId
5) Emits diagnostics for attribute errors

An assembly that declares no trace metadata gets no generated file at all - no empty provider and no
module initializer.

## TraceId attribute

```csharp
using EmberTrace.Abstractions.Attributes;

[assembly: TraceId(1000, "App", "App")]
[assembly: TraceId(2100, "IoWait", "IO")]
```

Signature:

- `id` (`int`) - event identifier
- `name` (`string`) - human-readable name
- `category` (`string?`) - optional (for grouping)

## Naming ids you already declare

If the ids already live in your code as constants, annotate them instead of repeating them at assembly
level. `[TraceName]` and `[TraceCategory]` apply to `const int` fields; without `[TraceName]` the field
name is used:

```csharp
using EmberTrace.Abstractions.Attributes;

static class Ids
{
    [TraceName("CPU work")]
    [TraceCategory("CPU")]
    public const int Cpu = 2100;

    [TraceCategory("IO")]
    public const int IoWait = 2200;
}
```

These fields feed the metadata provider only - they are constants already, so they are never re-emitted
into `TraceIds.g.cs`.

## Setup

```bash
dotnet add package EmberTrace.Abstractions
dotnet add package EmberTrace.Generator
```

### TraceIds generation

Add to the project:

```xml
<PropertyGroup>
  <EmberTraceGenerateTraceIds>true</EmberTraceGenerateTraceIds>
</PropertyGroup>
```

The generator creates `TraceIds.g.cs` with `const int` fields. Names are normalized, and collisions get
a suffix. Entries are ordered by `id`, so adding an attribute above an existing one never renames a
constant that your code already uses.

### Diagnostics

- **ETG001** (error) - the same `id` is declared more than once
- **ETG002** (warning) - `name` is empty
- **ETG003** (warning) - `category` is empty
- **ETG004** (warning) - `TraceId` arguments are not a constant `int` id and `string` name, so the
  attribute is skipped; the rest of the assembly still generates
- **ETG005** (warning) - `[TraceName]` / `[TraceCategory]` sit on a field that is not `const int`
- **ETG006** (warning) - two names normalize to the same constant name; the later one gets a suffix

## The global registry

Every instrumented assembly registers its own provider through its own `ModuleInitializer`, so a process
with twenty of them ends up with twenty registrations. They are not consulted one by one: on the first
resolve after a registration changes, `TraceMetadata` folds every provider that also implements
`IEnumerable<TraceMeta>` - the generated ones do - into a single `FrozenDictionary<int, TraceMeta>`.
Resolving an id is then one lookup, no matter how many assemblies are instrumented. Providers that
cannot enumerate their contents (`EnableRuntimeMetadata` uses one) stay behind that dictionary as an
ordered fallback chain.

The snapshot is cached and rebuilt lazily, so registering late is cheap and correct:

- `TraceMetadata.Register(provider)` - add a provider and invalidate the snapshot
- `TraceMetadata.Unregister(provider)` - remove a provider previously registered, `false` if it was not
- `TraceMetadata.Reset()` - drop every registration; intended for tests that must not leak providers
  into each other

## If generator is not connected

`Tracer.CreateMetadata()` returns an empty provider (without names). This is fine - the trace remains valid,
but reports/export are less readable.

See also:
- [Quick Start](../../guides/getting-started/README.md)
- [Usage and API](../../guides/usage/README.md)

## Screenshots

![Сгенерированный код: файл из `obj/` с атрибутами и регистрацией (вид в IDE)](../../assets/generator-generated-code.png)
