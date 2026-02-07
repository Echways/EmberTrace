English version: [./README.md](./README.md)

# Flow и async

**Flow** — это связь между кусками работы, которые происходят не в одном стеке вызовов:
- разные потоки
- `Task.Run`
- продолжения после `await`

В Chrome Trace/Perfetto Flow обычно отображается как «стрелка» или связанная цепочка событий.

## API: FlowHandle (удобно)

```csharp
var flow = Tracer.FlowStartNewHandle(Ids.JobFlow);

flow.Step(); // отметка прогресса
DoWork();

await Task.Delay(10);
flow.Step();

flow.End();
```

`FlowHandle` удобен, когда ты хочешь держать один объект-цепочку и добавлять шаги по мере выполнения.

## API: FlowScope (scope‑стиль)

```csharp
using var flow = Tracer.Flow(Ids.JobFlow);
flow.Step();
```

`FlowScope` автоматически завершает flow в `Dispose()`. Можно получить `FlowHandle` через `ToHandle()`,
если нужно передать цепочку между потоками.

## API: явный `flowId` (когда нужно передавать id)

```csharp
var flowId = Tracer.NewFlowId();

Tracer.FlowStart(Ids.JobFlow, flowId);
// ...
Tracer.FlowStep(Ids.JobFlow, flowId);
// ...
Tracer.FlowEnd(Ids.JobFlow, flowId);
```

## Activity bridge

Если используется `Activity.Current`, можно связать flow с текущим trace id:

```csharp
Tracer.FlowFromActivityCurrent(Ids.JobFlow);
```

## Практические правила

- Всегда закрывай flow (`End`), иначе цепочка будет «оборвана» в визуализации.
- Flow — это **связь**, а не длительность. Длительность даёт `Scope`.
- Хороший кейс: связать «приняли запрос» → «поставили в очередь» → «обработали» → «ответили».

См. также:
- [Экспорт](../../guides/export/README.ru.md)
- [Использование и API](../../guides/usage/README.ru.md)

## Скриншоты

![Диаграмма Flow: создание/пропагация FlowId между async/threads](../../assets/flows-propagation.png)
