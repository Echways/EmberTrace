English version: [./README.md](./README.md)

# Генератор и TraceId (метаданные)

`EmberTrace` работает с `int id`. Чтобы в экспорте/отчётах были **имена** и **категории**,
используются assembly-атрибуты и source generator.

## Что делает генератор

`EmberTrace.Generator`:

1) Сканирует проект на `[assembly: TraceId(id, name, category)]` и на поля `const int`,
   помеченные `[TraceName]` / `[TraceCategory]`
2) Генерирует провайдер метаданных (`ITraceMetadataProvider`)
3) **Автоматически регистрирует** его через `ModuleInitializer`, так что `Tracer.CreateMetadata()`
   начнёт возвращать имена/категории без ручной инициализации.
4) Опционально генерирует `TraceIds.g.cs` с константами `const int` для каждого TraceId
5) Выдаёт диагностики по ошибкам атрибутов

Если в сборке нет ни одного объявления метаданных, генератор не выдаёт вообще ничего — ни пустого
провайдера, ни `ModuleInitializer`.

## Атрибут TraceId

```csharp
using EmberTrace.Abstractions.Attributes;

[assembly: TraceId(1000, "App", "App")]
[assembly: TraceId(2100, "IoWait", "IO")]
```

Сигнатура:

- `id` (`int`) — идентификатор события
- `name` (`string`) — человекочитаемое имя
- `category` (`string?`) — опционально (для группировки)

## Имена для уже объявленных id

Если id уже живут в коде как константы, размечай их вместо дублирования на уровне сборки.
`[TraceName]` и `[TraceCategory]` применяются к полям `const int`; без `[TraceName]` берётся имя поля:

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

Такие поля попадают только в провайдер метаданных — они уже константы, поэтому в `TraceIds.g.cs`
повторно не выносятся.

## Подключение

```bash
dotnet add package EmberTrace.Abstractions
dotnet add package EmberTrace.Generator
```

### Генерация TraceIds

Добавь в проект:

```xml
<PropertyGroup>
  <EmberTraceGenerateTraceIds>true</EmberTraceGenerateTraceIds>
</PropertyGroup>
```

Генератор создаст файл `TraceIds.g.cs` с `const int` полями. Имена нормализуются, а при коллизиях
добавляется суффикс. Элементы упорядочены по `id`, поэтому новый атрибут, добавленный выше уже
существующего, не переименовывает константу, которой пользуется код.

### Диагностики

- **ETG001** (ошибка) — один и тот же `id` встречается больше одного раза
- **ETG002** (warning) — пустой `name`
- **ETG003** (warning) — пустой `category`
- **ETG004** (warning) — аргументы `TraceId` не являются константными `int` id и `string` name, атрибут
  пропускается; остальная сборка генерируется как обычно
- **ETG005** (warning) — `[TraceName]` / `[TraceCategory]` стоят на поле, которое не `const int`
- **ETG006** (warning) — два имени нормализуются в одно и то же имя константы; второму добавляется суффикс

## Глобальный реестр

Каждая инструментированная сборка регистрирует свой провайдер через свой `ModuleInitializer`, поэтому в
процессе с двадцатью такими сборками окажется двадцать регистраций. Они не опрашиваются по очереди: при
первом обращении после изменения регистраций `TraceMetadata` сворачивает все провайдеры, которые
дополнительно реализуют `IEnumerable<TraceMeta>` (генерируемые — реализуют), в один
`FrozenDictionary<int, TraceMeta>`. После этого разрешение id — один поиск независимо от числа сборок.
Провайдеры, которые не умеют перечислять своё содержимое (такой используется при
`EnableRuntimeMetadata`), остаются за этим словарём упорядоченной цепочкой fallback.

Снимок кэшируется и перестраивается лениво, поэтому поздняя регистрация дёшева и корректна:

- `TraceMetadata.Register(provider)` — добавить провайдер и инвалидировать снимок
- `TraceMetadata.Unregister(provider)` — удалить ранее зарегистрированный провайдер, `false` если его не было
- `TraceMetadata.Reset()` — сбросить все регистрации; нужно тестам, чтобы провайдеры не протекали между ними

## Если генератор не подключён

`Tracer.CreateMetadata()` вернёт пустой провайдер (без имён). Это нормально — трасса всё равно корректна,
но отчёты/экспорт будут менее читаемыми.

См. также:
- [Быстрый старт](../../guides/getting-started/README.ru.md)
- [Использование и API](../../guides/usage/README.ru.md)

## Скриншоты

![Сгенерированный код: файл из `obj/` с атрибутами и регистрацией (вид в IDE)](../../assets/generator-generated-code.png)
