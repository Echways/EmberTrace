using System;
using System.IO;
using System.Threading.Tasks;
using EmberTrace;
using EmberTrace.Abstractions.Attributes;
using EmberTrace.Export;
using EmberTrace.ReportText;

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
Console.WriteLine("== EmberTrace.Demo ==");

Tracer.Start();

await using (Tracer.ScopeAsync(Ids.App))
{
    CpuWork(120_000);
    await IoWaitAsync(40);
    CpuWork(80_000);
}

var session = Tracer.Stop();
var meta = Tracer.CreateMetadata();

var processed = session.Process();
Console.WriteLine(TraceText.Write(processed, meta: meta, topHotspots: 10, maxDepth: 4));

var chromePath = Path.Combine("out", "trace.json");
using (var fs = File.Create(chromePath))
    TraceExport.WriteChromeComplete(session, fs, meta: meta);

Console.WriteLine("OK: " + chromePath);

static class Ids
{
    public const int App = 1000;
    public const int Cpu = 1100;
    public const int Io = 1200;
}
