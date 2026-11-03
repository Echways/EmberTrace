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

## Streaming and compression

`TraceFormat.OpenRead` returns a `TraceStream`: the header, thread names, metadata and environment are available at
once, and `Events` decodes the file lazily, in file order (already sorted by time), without building a `TraceSession`.
Use it for traces that do not fit comfortably in memory.

```csharp
using var trace = TraceFormat.OpenRead("out/session.ember.gz");
var scopes = new ScopeReader(trace.Events, trace.EndTimestamp);
```

`Events` can be enumerated once. A path ending in `.ember.gz` is written gzip-compressed (`CompressionLevel.Fastest`);
`Read` and `OpenRead` recognise gzip by its signature, whatever the file is called. A truncated or corrupt gzip file
raises `InvalidDataException`. `OpenRead` requires thread names and metadata to precede the events, as the writer
produces them; `Read` has no such requirement.

## Why not Chrome Trace JSON

Chrome Trace JSON is a display format: it is lossy, verbose, and slow to parse. `.ember` keeps every field of every
event, is several times smaller, and reloads into the same analysis pipeline you would run in-process.

## What is stored

- Header: format version, timestamp frequency, session window, the UTC instant the session started, event count, and
  the drop/sampling counters.
- Thread names.
- Metadata (name and category) for every id that actually appears in the trace.
- Environment (format version 2): the machine and runtime settings at start and at stop (OS, runtime, GC, tiered
  compilation, `DOTNET_*` variables, CPU governor and turbo on Linux, affinity, cgroup limits) and the
  `SessionOptions.Tags` you set, under the `tag.` prefix. `EnvironmentCompatibility.Compare` reports which differences
  between two recordings make a timing comparison unreliable.
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

The header records its own size. Readers skip header bytes they do not understand, so files written by a newer build
of the same format version stay readable; a file written before the UTC start was recorded loads with
`StartedAtUtc == null`.

Format version 2 adds the environment section. The reader accepts versions 1 and 2; a version 1 file loads with
`Environment == null`, and comparing it with another recording yields a warning, not an error. The writer always
writes version 2, so a file it writes is refused by older builds. An unknown section id in any version is an
`InvalidDataException`.
