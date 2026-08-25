Русская версия: [./README.ru.md](./README.ru.md)

# Binary session format (`.ember`)

`EmberTrace.Format` persists a stopped session to a compact binary file and reads it back into a fully analyzable
`TraceSession`.

```bash
dotnet add package EmberTrace.Format
```

## Saving and loading

```csharp
var session = Tracer.Stop();

TraceFormat.Write(session, "out/session.ember");

var reloaded = TraceFormat.Read("out/session.ember");
Console.WriteLine(TraceText.Write(reloaded.Process(), reloaded.Metadata));
```

Stream overloads are available in both directions:

```csharp
using var fs = File.Create("out/session.ember");
TraceFormat.Write(session, fs);
```

## Why not Chrome Trace JSON

Chrome Trace JSON is a display format: it is lossy, verbose, and slow to parse. `.ember` keeps every field of every
event, is several times smaller, and reloads into the same analysis pipeline you would run in-process.

## What is stored

- Header: format version, timestamp frequency, session window, event count, and the drop/sampling counters.
- Thread names.
- Metadata (name and category) for every id that actually appears in the trace.
- All events, in globally sorted order.

## What is not stored

- `SessionOptions`. It holds delegates and only affects recording; a loaded session carries default options.
- The original chunk layout. Events round-trip as a sorted sequence, so compare `EnumerateEventsSorted()` rather than
  `EnumerateEvents()` when asserting equality.

## Timestamps across machines

The recording machine's `Stopwatch.Frequency` is stored in the header and travels with the file, so durations are
computed correctly when a trace recorded on one machine is analyzed on another.

## Compatibility

The header carries a format version. A reader refuses a file written by a newer version with a clear
`InvalidDataException` instead of misinterpreting it. Malformed or truncated files raise `InvalidDataException` or
`EndOfStreamException`.
