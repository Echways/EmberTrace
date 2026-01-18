# EmberTrace.Generator

Roslyn Incremental Source Generator для EmberTrace.

Генератор сканирует проект на наличие атрибутов из `EmberTrace.Abstractions` и генерирует:
- `TraceIds.g.cs` — набор `const int` для быстрых идентификаторов.
- `TraceMetadata.g.cs` — таблицу метаданных `id -> name/category` для отчётов (cold path).

## Входные данные
- Символы, помеченные `[TraceId]`.
- Опционально: `[TraceName]`, `[TraceCategory]`.
- Опционально: явный `Id` в `[TraceId]` (override).

## Правила вычисления ID
- По умолчанию ID вычисляется детерминированным хэшем от “стабильного полного имени символа”
  (namespace, type, method, сигнатура).
- Если указан явный `Id`, он имеет приоритет.
- Коллизии проверяются на этапе генерации.
  При обнаружении коллизии генератор выдаёт diagnostic error и предлагает задать явный `Id`.

## Выходные файлы
### `TraceIds.g.cs`
`public static class TraceIds` с `public const int ...`

### `TraceMetadata.g.cs`
Тип, реализующий контракт runtime метаданных (например `ITraceMetadata`), содержащий:
- список всех `id`
- имена
- категории (если задано)

## Организация кода в генераторе
- `SymbolDiscovery` — поиск и извлечение данных по символам.
- `NameFormatting` — стабильное имя и “красивое имя” для отчётов.
- `IdComputation` — хэш/override.
- `CollisionDetector` — проверка уникальности id.
- `Diagnostics` — сообщения компилятора.
- `SourceEmitter` — сборка финального исходника `.g.cs`.

## Ограничения
- Генератор не переписывает тела методов.
  Инструментация выполняется вручную через `Profiler.Scope(TraceIds.X)` или через отдельный механизм.
