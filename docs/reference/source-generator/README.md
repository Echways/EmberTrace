# Генератор и TraceId (метаданные)

`EmberTrace` работает с `int id`. Чтобы в экспорте/отчётах были **имена** и **категории**,
используются assembly-атрибуты и source generator.

## Что делает генератор

`EmberTrace.Generator`:

1) Сканирует проект на `[assembly: TraceId(id, name, category)]`
2) Генерирует провайдер метаданных (`ITraceMetadataProvider`)
3) **Автоматически регистрирует** его через `ModuleInitializer`, так что `Tracer.CreateMetadata()`
   начнёт возвращать имена/категории без ручной инициализации.

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

## Подключение

```bash
dotnet add package EmberTrace.Abstractions
dotnet add package EmberTrace.Generator
```

## Если генератор не подключён

`Tracer.CreateMetadata()` вернёт пустой провайдер (без имён). Это нормально — трасса всё равно корректна,
но отчёты/экспорт будут менее читаемыми.

См. также:
- [Быстрый старт](../../guides/getting-started/README.md)
- [Использование и API](../../guides/usage/README.md)

## Скриншоты

![Сгенерированный код: файл из `obj/` с атрибутами и регистрацией (вид в IDE)](../../assets/generator-generated-code.png)

