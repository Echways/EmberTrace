Русская версия: [./README.ru.md](./README.ru.md)

# Flow and async

**Flow** is a link between pieces of work that do not happen in the same call stack:
- different threads
- `Task.Run`
- continuations after `await`

In Chrome Trace/Perfetto, a flow is usually displayed as an "arrow" or a connected event chain.

## API: FlowHandle (convenient)

```csharp
var flow = Tracer.FlowStartNewHandle(Ids.JobFlow);

flow.Step(); // отметка прогресса
DoWork();

await Task.Delay(10);
flow.Step();

flow.End();
```

`FlowHandle` is convenient when you want to keep one chain object and add steps as execution progresses.

## API: FlowScope (scope style)

```csharp
using var flow = Tracer.Flow(Ids.JobFlow);
flow.Step();
```

`FlowScope` automatically completes the flow in `Dispose()`. You can get a `FlowHandle` via `ToHandle()`
if you need to pass the chain between threads.

## API: explicit `flowId` (when you need to pass the id)

```csharp
var flowId = Tracer.NewFlowId();

Tracer.FlowStart(Ids.JobFlow, flowId);
// ...
Tracer.FlowStep(Ids.JobFlow, flowId);
// ...
Tracer.FlowEnd(Ids.JobFlow, flowId);
```

## Activity bridge

If `Activity.Current` is used, you can link a flow to the current trace id:

```csharp
Tracer.FlowFromActivityCurrent(Ids.JobFlow);
```

## Practical rules

- Always close a flow (`End`), otherwise the chain will look "broken" in visualization.
- Flow is a **link**, not a duration. Duration is represented by `Scope`.
- A good use case: link "request accepted" -> "queued" -> "processed" -> "responded".

See also:
- [Export](../../guides/export/README.md)
- [Usage and API](../../guides/usage/README.md)

## Screenshots

![Диаграмма Flow: создание/пропагация FlowId между async/threads](../../assets/flows-propagation.png)
