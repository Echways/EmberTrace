Русская версия: [./README.ru.md](./README.ru.md)

# Performance testing and regression gates

`EmberTrace.Testing` turns a trace into assertions you can run in CI. It does not depend on a test framework — every
failure is a `TraceAssertionException`, which MSTest, xUnit and NUnit all report as a failed test.

```bash
dotnet add package EmberTrace.Testing
```

## Asserting a budget

```csharp
Tracer.Start();
RunTheScenario();
var stats = Tracer.Stop().Analyze();

stats.Scope(Ids.DbQuery, meta)
    .CountAtMost(3)
    .P95MsUnder(5)
    .P99MsUnder(12);
```

Failures name the scope and both numbers:

```
Trace assertion failed: expected 'DbQuery' (id 2100) p95 to be under 5.000 ms, but it was 8.240 ms.
```

Available assertions: `CountExactly`, `CountAtMost`, `CountAtLeast`, `NotRecorded`, `TotalMsUnder`, `AverageMsUnder`,
`MaxMsUnder`, `P50MsUnder`, `P95MsUnder`, `P99MsUnder`, and `PercentileMsUnder(percentile, ms)` for anything else.

Every threshold assertion fails when the id was never recorded; use `NotRecorded()` to assert the opposite. Chain calls
freely — each one returns the same assertion.

## Comparing against a baseline

Absolute percentile thresholds are brittle across machines. Comparing two runs is usually the better gate:

```csharp
var comparison = TraceDiff.Compare(baselineStats, currentStats);
Console.WriteLine(TraceDiff.Format(comparison, meta, minPercent: 5));

TraceBudget.AssertNoRegressions(baselineStats, currentStats, maxPercent: 10, meta);
```

`Compare` returns every id from either run, worst total-time regression first. `RegressionsOver(percent)` and
`AssertNoRegressions` look only at ids recorded in *both* runs: an id that was added or removed between runs is shown
by `Format` (marked `(new)` or `(gone)`) but never fails the gate, since there is no baseline to compare it against.

When a baseline value is zero and the current one is positive, the change is `PositiveInfinity`; for ids present on one
side only it is `NaN`.

## Storing baselines

Persist the recorded session with `EmberTrace.Format` and re-analyze it later:

```csharp
TraceFormat.Write(session, "baselines/checkout.ember");

var baseline = TraceFormat.Read("baselines/checkout.ember").Analyze();
```

## Percentile accuracy

Durations are bucketed in a log-linear histogram with 5 significant bits, so a reported percentile is within 3.125% of
the true value and is always rounded **up** — a gate never passes because a latency was under-reported. Durations below
64 ticks are recorded exactly, and `MinMs`/`MaxMs` are always exact.

See also:
- [Analysis and reports](../analysis/README.md)
- [Binary session format (.ember)](../format/README.md)
