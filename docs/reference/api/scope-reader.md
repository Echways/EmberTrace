Русская версия: [./scope-reader.ru.md](./scope-reader.ru.md)

# ScopeReader

`EmberTrace.Sessions.ScopeReader` reconstructs scopes from a raw event stream. `Analyze`, `Process`,
the Chrome exporter, and the OpenTelemetry exporter are all built on it, so every consumer sees the same
nesting, the same durations, and the same mismatch counters.

Reconstruction is not thread-based:

- a synchronous scope belongs to the track `(writer track, enclosing async scope)`;
- an async scope is matched by its own `AsyncScopeId`, so its `Begin` and `End` may sit on different threads;
- a scope opened inside an async scope becomes its child, even when it runs on another thread.

A track is one writer inside one session, not a managed thread id. The runtime recycles
`Environment.CurrentManagedThreadId` after a thread dies, so grouping by thread id would let a new
thread close a frame left open by a dead one. `TrackId` never collides that way; `ThreadId` is kept
for display only.

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

| Member | Meaning |
| --- | --- |
| `Kind` | `Open` on `Begin`, `Close` when the scope is closed |
| `Id` | trace id of the scope |
| `ParentId`, `Depth`, `Index` | position in the reconstructed tree |
| `TrackId`, `EndTrackId` | writer track of `Begin` and of `End` — the grouping key |
| `ThreadId`, `EndThreadId` | managed thread id of `Begin` and of `End` — for display only |
| `StartTimestamp`, `EndTimestamp`, `DurationTicks` | timing |
| `AsyncScopeId`, `IsAsync` | identity of an async scope (`0` for synchronous ones) |
| `IsSynthetic` | the scope was never closed and was force-closed by the reader |
| `Tag`, `ParentTag` | consumer state attached to the frame and to its parent |

Counters (`TotalEvents`, `UnmatchedBeginCount`, `UnmatchedEndCount`, `MismatchedEndCount`, `Tracks`)
are filled while `Read()` is being enumerated and are final once enumeration completes. `Tracks` maps
every track id seen to the managed thread id that wrote it.
