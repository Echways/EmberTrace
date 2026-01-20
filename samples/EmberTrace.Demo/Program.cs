using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EmberTrace.Abstractions.Attributes;
using EmberTrace;

[assembly: TraceId(Ids.App, "App", "App")]
[assembly: TraceId(Ids.Warmup, "Warmup", "App")]
[assembly: TraceId(Ids.Producer, "Producer", "Flow")]
[assembly: TraceId(Ids.Consumer, "Consumer", "Flow")]
[assembly: TraceId(Ids.JobFlow, "JobFlow", "Flow")]
[assembly: TraceId(Ids.Cpu, "Cpu", "CPU")]
[assembly: TraceId(Ids.IO, "IO", "IO")]

static void Busy(int spins)
{
    using var s = Tracer.Scope(Ids.Cpu);
    Thread.SpinWait(spins);
}

static void SimulatedIo(int ms)
{
    using var s = Tracer.Scope(Ids.IO);
    Thread.Sleep(ms);
}

static void ProducerWork()
{
    using var s = Tracer.Scope(Ids.Producer);
    Busy(180_000);
    SimulatedIo(5);
}

static void ConsumerWork(int n)
{
    using var s = Tracer.Scope(Ids.Consumer);
    for (int i = 0; i < n; i++)
    {
        if ((i & 1) == 0)
            Busy(120_000);
        else
            SimulatedIo(6);
    }
}

Tracer.Start();

using (Tracer.Scope(Ids.App))
{
    using (Tracer.Scope(Ids.Warmup))
    {
        Busy(120_000);
        SimulatedIo(4);
    }

    var flow = Tracer.FlowStartNewHandle(Ids.JobFlow);
    ProducerWork();
    flow.Step();

    var t = Task.Run(() =>
    {
        Tracer.FlowEnd(flow);
        ConsumerWork(5);
    });

    Busy(140_000);
    t.Wait();
}

var session = Tracer.Stop();

var processed = session.Process();
var meta = Tracer.CreateMetadata();

Console.WriteLine(TraceText.Write(processed, meta: meta, topHotspots: 20, maxDepth: 6));

var outPath = Path.Combine(AppContext.BaseDirectory, "embertrace_flowpair_complete.json");
using (var fs = File.Create(outPath))
    TraceExport.WriteChromeComplete(session, fs, meta: meta);

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