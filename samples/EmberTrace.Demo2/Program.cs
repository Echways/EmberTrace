using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EmberTrace;
using EmberTrace.Abstractions.Attributes;

[assembly: TraceId(Ids.App, "App", "App")]
[assembly: TraceId(Ids.Warmup, "Warmup", "App")]
[assembly: TraceId(Ids.Worker, "Worker", "Workers")]
[assembly: TraceId(Ids.Fib, "Fib", "CPU")]
[assembly: TraceId(Ids.Sort, "Sort", "CPU")]
[assembly: TraceId(Ids.BusyWait, "BusyWait", "CPU")]
[assembly: TraceId(Ids.Io, "IO", "IO")]
[assembly: TraceId(Ids.Cpu, "Cpu", "CPU")]


static int Fib(int n)
{
    using var s = Tracer.Scope(Ids.Fib);
    if (n <= 1) return n;
    return Fib(n - 1) + Fib(n - 2);
}

static void Busy(int spins)
{
    using var s = Tracer.Scope(Ids.BusyWait);
    Thread.SpinWait(spins);
}

static int[] MakeData(int n, int seed)
{
    var r = new Random(seed);
    var a = new int[n];
    for (int i = 0; i < a.Length; i++)
        a[i] = r.Next();
    return a;
}

static void SortWork(int n, int seed)
{
    using var s = Tracer.Scope(Ids.Sort);
    var a = MakeData(n, seed);
    Array.Sort(a);
}

static void SimulatedIo(int ms)
{
    using var s = Tracer.Scope(Ids.Io);
    Thread.Sleep(ms);
}

static void CpuWork(int fibN, int sortN, int seed)
{
    using var s = Tracer.Scope(Ids.Cpu);
    _ = Fib(fibN);
    SortWork(sortN, seed);
    Busy(40_000);
}

static void Worker(int workerId, int iterations)
{
    using var s = Tracer.Scope(Ids.Worker);

    var sw = Stopwatch.StartNew();
    for (int i = 0; i < iterations; i++)
    {
        if ((i & 1) == 0)
            CpuWork(fibN: 20, sortN: 15_000, seed: workerId * 1000 + i);
        else
            SimulatedIo(ms: 5);
    }

    sw.Stop();
}

Tracer.Start();

using (var app = Tracer.Scope(Ids.App))
{
    using (var warmup = Tracer.Scope(Ids.Warmup))
    {
        Busy(200_000);
        SortWork(5_000, seed: 123);
    }

    var t1 = Task.Run(() => Worker(workerId: 1, iterations: 8));
    var t2 = Task.Run(() => Worker(workerId: 2, iterations: 8));
    Task.WaitAll(t1, t2);
}

var session = Tracer.Stop();
var processed = session.Process();

var meta = Tracer.CreateMetadata();
Console.WriteLine(TraceText.Write(processed, meta: meta, topHotspots: 12, maxDepth: 4));

var path = Path.Combine(AppContext.BaseDirectory, "embertrace_complete.json");
using var fs = File.Create(path);
TraceExport.WriteChromeComplete(session, fs, meta: meta);
Console.WriteLine(path);

static class Ids
{
    public const int App = 1000;
    public const int Warmup = 1100;
    public const int Worker = 1200;

    public const int Fib = 2001;
    public const int Sort = 2002;
    public const int BusyWait = 2003;

    public const int Io = 3001;
    public const int Cpu = 3002;
}