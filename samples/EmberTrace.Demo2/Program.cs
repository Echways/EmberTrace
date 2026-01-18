using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EmberTrace;
using EmberTrace.Public;
using EmberTrace.Reporting;
using EmberTrace.Reporting.Text;

static int Fib(int n)
{
    using var s = Profiler.Scope(Ids.Fib);
    if (n <= 1) return n;
    return Fib(n - 1) + Fib(n - 2);
}

static void Busy(int spins)
{
    using var s = Profiler.Scope(Ids.BusyWait);
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
    using var s = Profiler.Scope(Ids.Sort);
    var a = MakeData(n, seed);
    Array.Sort(a);
}

static void SimulatedIo(int ms)
{
    using var s = Profiler.Scope(Ids.Io);
    Thread.Sleep(ms);
}

static void CpuWork(int fibN, int sortN, int seed)
{
    using var s = Profiler.Scope(Ids.Cpu);
    _ = Fib(fibN);
    SortWork(sortN, seed);
    Busy(40_000);
}

static void Worker(int workerId, int iterations)
{
    using var s = Profiler.Scope(Ids.Worker);

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

var opts = new SessionOptions
{
    ChunkCapacity = 32_768,
    OverflowPolicy = OverflowPolicy.Drop
};

Profiler.Start(opts);

using (var app = Profiler.Scope(Ids.App))
{
    using (var warmup = Profiler.Scope(Ids.Warmup))
    {
        Busy(200_000);
        SortWork(5_000, seed: 123);
    }

    var t1 = Task.Run(() => Worker(workerId: 1, iterations: 8));
    var t2 = Task.Run(() => Worker(workerId: 2, iterations: 8));
    Task.WaitAll(t1, t2);
}

var session = Profiler.Stop();
var processed = session.Process();

var names = new System.Collections.Generic.Dictionary<int, string>
{
    [Ids.App] = "App",
    [Ids.Warmup] = "Warmup",
    [Ids.Worker] = "Worker",
    [Ids.Fib] = "Fib",
    [Ids.Sort] = "Sort",
    [Ids.BusyWait] = "BusyWait",
    [Ids.Io] = "IO",
    [Ids.Cpu] = "Cpu"
};

Console.WriteLine(TextReportWriter.Write(
    processed,
    nameOf: id => names.TryGetValue(id, out var n) ? n : null,
    topHotspots: 12,
    maxDepth: 4));


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