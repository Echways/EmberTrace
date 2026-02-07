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

## Code fix

For `ETA001` and `ETA002`, fixes are available that automatically wrap a call in `using/await using`.
