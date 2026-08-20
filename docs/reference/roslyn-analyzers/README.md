Русская версия: [./README.ru.md](./README.ru.md)

# Roslyn analyzers

Package `EmberTrace.RoslynAnalyzers` helps catch common API usage mistakes.

## Installation

```bash
dotnet add package EmberTrace.RoslynAnalyzers
```

The package includes code fixes in a separate assembly. They are not used in CLI builds and do not affect compilation.

## Diagnostics

- **ETA001** - `Scope` is created but not wrapped in `using`
- **ETA002** - `AsyncScope` is created without `await using`
- **ETA003** - `FlowHandle` is created but `End/TryEnd` is not called

`ETA001` and `ETA002` look at how the call itself is bound - a `using` block somewhere up the tree
(a file, a connection, a lock) does not dispose a scope declared inside it, and is reported.

## Code fix

`ETA001` and `ETA002` are fixable, and the fix depends on the shape of the call:

- `var scope = Tracer.Scope(id);` gets the `using` (or `await using`) keyword added to the declaration,
  which keeps the scope alive to the end of the enclosing block.
- A bare `Tracer.Scope(id);` statement is turned into `using (Tracer.Scope(id)) { ... }` wrapping the
  statements that follow it in the block, so the scope measures them.
