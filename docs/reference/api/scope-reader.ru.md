English version: [./scope-reader.md](./scope-reader.md)

# ScopeReader

`EmberTrace.Sessions.ScopeReader` восстанавливает scope из сырого потока событий. На нём построены
`Analyze`, `Process`, Chrome-экспортёр и OpenTelemetry-экспортёр — поэтому все потребители видят одинаковую
вложенность, одинаковые длительности и одинаковые счётчики рассогласований.

Восстановление не привязано к потоку:

- синхронный scope принадлежит дорожке `(дорожка писателя, объемлющий async scope)`;
- async scope сопоставляется по собственному `AsyncScopeId`, поэтому его `Begin` и `End` могут быть на разных потоках;
- scope, открытый внутри async scope, становится его ребёнком, даже если выполняется на другом потоке.

Дорожка — это один писатель внутри одной сессии, а не managed thread id. Рантайм переиспользует
`Environment.CurrentManagedThreadId` после гибели потока, поэтому группировка по id потока позволила бы
новому потоку закрыть кадр, оставленный открытым мёртвым. `TrackId` так не сталкивается; `ThreadId`
остаётся только для отображения.

```csharp
var reader = new ScopeReader(session, strict: false);

foreach (var step in reader.Read())
{
    if (step.Kind == ScopeStepKind.Open)
    {
        step.Tag = new MySpan(step.Id, step.ParentTag as MySpan);
        continue;
    }

    if (step.IsSynthetic)
        continue;

    Report(step.Id, step.DurationTicks);
}

Console.WriteLine(reader.UnmatchedBeginCount + reader.UnmatchedEndCount + reader.MismatchedEndCount);
```

## ScopeStep

| Член | Значение |
| --- | --- |
| `Kind` | `Open` на `Begin`, `Close` при закрытии scope |
| `Id` | trace id scope |
| `ParentId`, `Depth`, `Index` | место в восстановленном дереве |
| `TrackId`, `EndTrackId` | дорожка писателя для `Begin` и для `End` — ключ группировки |
| `ThreadId`, `EndThreadId` | managed thread id для `Begin` и `End` — только для отображения |
| `StartTimestamp`, `EndTimestamp`, `DurationTicks` | тайминги |
| `AsyncScopeId`, `IsAsync` | идентичность async scope (`0` для синхронных) |
| `IsSynthetic` | scope не был закрыт и закрыт принудительно ридером |
| `Tag`, `ParentTag` | состояние потребителя на кадре и на его родителе |

Счётчики (`TotalEvents`, `UnmatchedBeginCount`, `UnmatchedEndCount`, `MismatchedEndCount`, `Tracks`)
заполняются по мере перечисления `Read()` и становятся окончательными после его завершения. `Tracks`
сопоставляет каждой встреченной дорожке managed thread id, который её писал.
