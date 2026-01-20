using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EmberTrace.Abstractions.Attributes;
using EmberTrace.Public;
using EmberTrace.Reporting;
using EmberTrace.Reporting.Export;
using EmberTrace.Reporting.Text;

[assembly: TraceId(Ids.App, "App", "App")]
[assembly: TraceId(Ids.Warmup, "Warmup", "App")]
[assembly: TraceId(Ids.Producer, "Producer", "Flow")]
[assembly: TraceId(Ids.Consumer, "Consumer", "Flow")]
[assembly: TraceId(Ids.JobFlow, "JobFlow", "Flow")]
[assembly: TraceId(Ids.Cpu, "Cpu", "CPU")]
[assembly: TraceId(Ids.IO, "IO", "IO")]

static void Busy(int spins)
{
    using var s = Profiler.Scope(Ids.Cpu);
    Thread.SpinWait(spins);
}

static void SimulatedIo(int ms)
{
    using var s = Profiler.Scope(Ids.IO);
    Thread.Sleep(ms);
}

static void ProducerWork()
{
    using var s = Profiler.Scope(Ids.Producer);
    Busy(180_000);
    SimulatedIo(5);
}

static void ConsumerWork(int n)
{
    using var s = Profiler.Scope(Ids.Consumer);
    for (int i = 0; i < n; i++)
    {
        if ((i & 1) == 0)
            Busy(120_000);
        else
            SimulatedIo(6);
    }
}

var opts = new SessionOptions { ChunkCapacity = 32_768, OverflowPolicy = OverflowPolicy.Drop };
Profiler.Start(opts);

using (Profiler.Scope(Ids.App))
{
    using (Profiler.Scope(Ids.Warmup))
    {
        Busy(120_000);
        SimulatedIo(4);
    }

    var pair = Profiler.FlowStartNewPair(Ids.JobFlow);
    ProducerWork();
    pair.Step();

    var t = Task.Run(() =>
    {
        Profiler.FlowEnd(pair);
        ConsumerWork(5);
    });

    Busy(140_000);
    t.Wait();
}

var session = Profiler.Stop();

var processed = session.Process();
var meta = TraceMetadata.CreateDefault();

Console.WriteLine(TextReportWriter.Write(processed, meta: meta, topHotspots: 20, maxDepth: 6));

var outPath = Path.Combine(AppContext.BaseDirectory, "embertrace_flowpair_complete.json");
using (var fs = File.Create(outPath))
    ChromeTraceExporter.WriteComplete(session, fs, meta: meta);

Console.WriteLine(outPath);

static class Ids
{
    public const int App = 1000;
    public const int Warmup = 1100;

    public const int Producer = 2000;
    public const int Consumer = 2100;

    public const int JobFlow = 9001;

    public const int Cpu = 3002;
    public const int IO = 3001;
}