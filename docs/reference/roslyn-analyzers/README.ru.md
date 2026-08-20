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

`ETA001` и `ETA002` смотрят на то, как связан сам вызов: `using`-блок где-то выше по дереву (файл,
соединение, лок) не освобождает scope, объявленный внутри него, и такой случай сообщается.

## Code fix

`ETA001` и `ETA002` исправляются, и фикс зависит от формы вызова:

- к `var scope = Tracer.Scope(id);` добавляется ключевое слово `using` (или `await using`) — scope
  живёт до конца блока, семантика сохраняется;
- голый `Tracer.Scope(id);` превращается в `using (Tracer.Scope(id)) { ... }`, куда переезжают
  следующие за ним statement-ы блока, чтобы scope действительно что-то измерял.
