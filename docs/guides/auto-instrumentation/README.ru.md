English version: [./README.md](./README.md)

# Автоинструментация через `[Trace]`

Объяви метод `partial`, помести на него `[Trace]` и перенеси тело в метод `…Core`. Генератор выпустит
обёртку, вычислит стабильный id и зарегистрирует имя и категорию.

## Было и стало

Вручную:

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

Через генератор:

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

Каталог id исчезает, асинхронный scope больше нельзя забыть или не дождаться, а `[Trace]` виден в
сигнатуре, а не закопан в теле.

## Соглашение `…Core`

Каждому `[Trace] partial` методу нужен метод с тем же именем плюс суффикс `Core`, с теми же
параметрами, теми же `ref`/`out`/`in` и тем же возвращаемым типом. Содержащий тип и все внешние типы
должны быть `partial`.

У обобщённого метода core обязан использовать **те же имена типовых параметров**: сопоставление идёт по
отрендеренным сигнатурам, поэтому `T PickCore<T>(T value)` подходит к `T Pick<T>(T value)`, а
`T PickCore<TItem>(TItem value)` — нет.

## Что генерируется

Синхронные методы получают обычный scope — `Tracer.Scope` и так возвращает инертный scope, когда сессия
не запущена, поэтому отдельный быстрый путь не нужен:

```csharp
public partial int Sum(int a, int b)
{
    using var __emberTraceScope = global::EmberTrace.Tracer.Scope(160062063);
    return SumCore(a, b);
}
```

Асинхронные методы получают обёртку, которая намеренно **не** `async`. Она ветвится по
`Tracer.IsRunning` и напрямую перенаправляет в core, поэтому при выключенной записи стейт-машина не
строится вообще:

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

`[DebuggerNonUserCode]` стоит только на хелпере: шаг внутрь трассируемого метода приводит в твоё тело
`…Core`, а сама обёртка остаётся видимой для Just My Code, потому что это объявление *и есть* твой метод.

## Имена, категории и id

- Имя по умолчанию — `TypeName.MethodName`, то есть `OrderService.GetAsync`.
- Категория разрешается в порядке: `[Trace(Category = "…")]`, `[TraceCategory]` на методе,
  `[TraceCategory]` на содержащем типе с обходом наружу по вложенным типам.
- Id — это FNV-1a от разрешённого имени, тот же хеш, что использует `Tracer.Id(name)`, поэтому
  `Tracer.Id("OrderService.GetAsync")` в рантайме адресует ровно тот scope, который выпустил генератор.
- `[Trace("checkout")]` задаёт имя, которое никогда не сдвинется. `[Trace(Id = 4100)]` фиксирует id явно.

Трассируемые методы попадают в тот же каталог, что и `[assembly: TraceId]` и размеченные поля
`const int`, поэтому на сборку выпускается один провайдер метаданных, а `ETG001` покрывает все
источники id сразу.

## Перегрузки

Две трассируемые перегрузки иначе делили бы имя и, значит, id. Если у типа больше одного трассируемого
метода с одним именем, каждый из них уточняется списком параметров: `OrderService.GetAsync(int)`,
`OrderService.GetAsync<T>(string, int)`.

Следствие стоит проговорить прямо: **добавление второй перегрузки переименовывает — и переопределяет id
— первой**. Там, где id обязан пережить рефакторинг, ставь `[Trace("…")]`.

## Асимметрия синхронного throw

Если `…Core` сам не `async` и бросает до первого `await`, нетрассируемый путь пробросит исключение
синхронно, а трассируемый вернёт faulted task. Так ведёт себя любой рукописный быстрый путь; это
задокументировано, а не замазано.

## `ConfigureAwait(false)`

Сгенерированный хелпер ждёт с `ConfigureAwait(false)`, поэтому продолжение, закрывающее scope, не
захватывает контекст синхронизации. На парность событий это не влияет: `AsyncScope` несёт
`scopeId`/`parentScopeId` — ровно для этого он и существует.

## Неподдерживаемые формы

| Форма | Диагностика |
|-------|-------------|
| `[Trace]` на методе, который не является `partial` объявлением без реализации | `ETG010` |
| Нет члена `…Core` с совместимой сигнатурой | `ETG011` |
| `ref`-возвраты, параметры `ref`/`out`/`in` или `ref struct` у async-методов, `readonly` async-члены, `unsafe`-методы, члены интерфейсов, `IAsyncEnumerable<T>` | `ETG012` |
| Содержащий или внешний тип не `partial` | `ETG013` |
| `[Trace]` на классе без однозначного интерфейса | `ETG014` |
| Член интерфейса неподдерживаемой формы проксируется декоратором без трассировки | `ETG015` (info) |

`IAsyncEnumerable<T>` отложен, а не отвергнут навсегда: scope там охватывал бы весь перебор, а не вызов,
и это отдельное семантическое решение.

## DI-декоратор

`[Trace]` на **классе** генерирует `TracedX : IX`, который оборачивает каждый трассируемый член
интерфейса, вместо разметки метод за методом:

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

даёт в том же namespace и с той же доступностью, что и класс:

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

Декоратор удобнее, когда инструментировать нужно сервис целиком на его границе и он резолвится через DI.
Пометодный `[Trace]` лучше, когда важны только отдельные методы или когда нужны внутренние вызовы:
**декоратор видит только вызовы, идущие через интерфейс**, поэтому метод, который сервис вызывает у
самого себя, для него невидим. Это единственное, что пометодный стиль делает лучше.

Правила:

- Интерфейс — единственный напрямую реализованный, либо указанный в `[Trace(Interface = typeof(IX))]`.
  Всё остальное — `ETG014`.
- Имена scope используют имя **класса** — `InventoryService.Reserve`, а не имя интерфейса, поэтому один
  класс можно инструментировать любым из двух способов, не получая два разных имени для одного метода.
- Свойства, события и индексаторы проксируются без scope. Члены неподдерживаемой формы проксируются без
  трассировки и сообщаются как `ETG015` (info).
- Обобщённый интерфейс даёт обобщённый декоратор вместе с ограничениями.
- `AddTracedInventoryService(this IServiceCollection services, ServiceLifetime lifetime)` выпускается
  **только если** `Microsoft.Extensions.DependencyInjection.IServiceCollection` есть в компиляции —
  генератор не может добавить ссылку на пакет за тебя. Он регистрирует декоратор под интерфейсом и
  создаёт конкретный сервис через `ActivatorUtilities.CreateInstance`, так что constructor injection
  продолжает работать, а нетрассируемый экземпляр в контейнер не попадает.

## Миграция существующего кода — `ETA004`

`ETA004` (info) отмечает не-partial метод, тело которого начинается с ручного `Tracer.Scope` /
`Tracer.ScopeAsync` — это ровно тот метод, который должен стать `[Trace] partial`. Фикс переименовывает
его в `…Core`, делает `private`, убирает ручной `using` и вставляет над ним объявление
`[Trace] partial`. Fix All мигрирует целый слой сервисов одним действием.

Фикс намеренно **не** добавляет директиву `using EmberTrace.Abstractions.Attributes;` и не помечает
содержащий тип как `partial`: и то и другое затем сообщается через `ETG010`/`ETG013` со своими фиксами
компилятора, а попытка сделать всё три вещи в одном code action делает Fix All хрупким.

## Накладные расходы

Замерено на `benchmarks/EmberTrace.Benchmarks`, 10 000 операций на вызов бенчмарка, сессия не запущена:

| Бенчмарк | Среднее | Аллокации |
|----------|---------|-----------|
| `Trace_Sync_SessionStopped` | 5.29 us | — |
| `Trace_Async_SessionStopped` | 11.72 us | 72 B |

72 байта — это собственная стейт-машина метода бенчмарка, а не аллокация на вызов: при остановленной
сессии асинхронная обёртка вообще не входит в `async`-метод.

См. также:
- [Генератор и TraceId (метаданные)](../../reference/source-generator/README.ru.md)
- [Roslyn-анализаторы](../../reference/roslyn-analyzers/README.ru.md)
- [Использование и API](../usage/README.ru.md)
