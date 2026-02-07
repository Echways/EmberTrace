English version: [./README.md](./README.md)

# Roslyn analyzers

Пакет `EmberTrace.RoslynAnalyzers` помогает ловить типовые ошибки использования API.

## Установка

```bash
dotnet add package EmberTrace.RoslynAnalyzers
```

Пакет включает code fixes отдельной сборкой. В CLI-сборках они не используются и не влияют на компиляцию.

## Диагностики

- **ETA001** — `Scope` создан, но не обёрнут в `using`
- **ETA002** — `AsyncScope` создан без `await using`
- **ETA003** — `FlowHandle` создан, но `End/TryEnd` не вызывается

## Code fix

Для `ETA001` и `ETA002` доступны фиксы, которые автоматически оборачивают вызов в `using/await using`.
