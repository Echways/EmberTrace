using EmberTrace;
using EmberTrace.Abstractions.Attributes;

[assembly: TraceId(Ids.App, "App", "App")]
[assembly: TraceId(Ids.Cpu, "CpuWork", "CPU")]
[assembly: TraceId(Ids.Io, "IoWait", "IO")]

static void CpuWork(int iters)
{
    using var _ = Tracer.Scope(Ids.Cpu);

    var x = 1;
    for (var i = 0; i < iters; i++)
        x = unchecked(x * 1664525 + 1013904223);

    if (x == 42)
        Console.WriteLine(x);
}

static async Task IoWaitAsync(int ms)
{
    await using var _ = Tracer.ScopeAsync(Ids.Io);
    await Task.Delay(ms).ConfigureAwait(false);
}

Directory.CreateDirectory("out");
Console.WriteLine("== EmberTrace.BasicTracing ==");

Tracer.Start();

await using (Tracer.ScopeAsync(Ids.App))
{
    for (var i = 0; i < 20; i++)
    {
        CpuWork(80_000 + i * 10_000);
        await IoWaitAsync(2 + i % 5);
    }
}

var session = Tracer.Stop();
var meta = Tracer.CreateMetadata();

var processed = session.Process();
Console.WriteLine(TraceText.Write(processed, meta, 10, 4, includePercentiles: true));

var chromePath = Path.Combine("out", "trace.json");
using (var fs = File.Create(chromePath))
{
    TraceExport.WriteChromeComplete(session, fs, meta);
}

Console.WriteLine("Chrome trace: " + chromePath);

var foldedPath = Path.Combine("out", "trace.folded");
using (var folded = File.CreateText(foldedPath))
{
    TraceText.WriteCollapsedStacks(processed, folded, meta);
}

Console.WriteLine("Collapsed stacks: " + foldedPath);

var tracePath = Path.Combine("out", "session" + TraceFormat.FileExtension);
TraceFormat.Write(session, tracePath);

var reloaded = TraceFormat.Read(tracePath);
Console.WriteLine($"Binary session: {tracePath}, {new FileInfo(tracePath).Length} bytes, reloaded {reloaded.EventCount} events.");

internal static class Ids
{
    public const int App = 1000;
    public const int Cpu = 1100;
    public const int Io = 1200;
}