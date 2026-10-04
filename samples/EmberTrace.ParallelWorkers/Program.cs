using EmberTrace;
using EmberTrace.Abstractions.Attributes;
using EmberTrace.Flow;
using EmberTrace.Sessions;

[assembly: TraceId(Ids.App, "App", "App")]
[assembly: TraceId(Ids.Warmup, "Warmup", "App")]
[assembly: TraceId(Ids.Worker, "Worker", "Workers")]
[assembly: TraceId(Ids.Fib, "Fib", "CPU")]
[assembly: TraceId(Ids.Sort, "Sort", "CPU")]
[assembly: TraceId(Ids.BusyWait, "BusyWait", "CPU")]
[assembly: TraceId(Ids.Io, "IO", "IO")]
[assembly: TraceId(Ids.Cpu, "Cpu", "CPU")]
[assembly: TraceId(Ids.Job, "Job", "Flow")]

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
    for (var i = 0; i < a.Length; i++)
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

static void Worker(int workerId, int iterations, FlowHandle job)
{
    using var s = Tracer.Scope(Ids.Worker);
    job.Step();

    for (var i = 0; i < iterations; i++)
        if ((i & 1) == 0)
            CpuWork(20, 15_000, workerId * 1000 + i);
        else
            SimulatedIo(5);

    job.Step();
}

Tracer.Start(new SessionOptions
{
    ChunkCapacity = 64 * 1024,
    RuntimeCounters = RuntimeCounters.All,
    RuntimeCounterInterval = TimeSpan.FromMilliseconds(20)
});

using (Tracer.Scope(Ids.App))
{
    using (Tracer.Scope(Ids.Warmup))
    {
        Busy(200_000);
        SortWork(5_000, 123);
    }

    var jobs = new[] { Tracer.FlowStartNewHandle(Ids.Job), Tracer.FlowStartNewHandle(Ids.Job) };
    var workers = jobs.Select((job, index) => Task.Run(() => Worker(index + 1, 8, job))).ToArray();
    Task.WaitAll(workers);

    foreach (var job in jobs)
        job.End();
}

var session = Tracer.Stop();
var processed = session.Process();

var meta = Tracer.CreateMetadata();
Console.WriteLine(TraceText.Write(processed, meta, 12, 4));

foreach (var flow in session.AnalyzeFlows())
    Console.WriteLine($"Flow {flow.FlowId}: {flow.TotalDurationMs:F3} ms across {flow.Steps.Count} steps");

Directory.CreateDirectory("out");
var path = Path.Combine("out", "trace.json");
using var fs = File.Create(path);
TraceExport.WriteChromeComplete(session, fs, meta);
Console.WriteLine("Chrome trace: " + path);

internal static class Ids
{
    public const int App = 1000;
    public const int Warmup = 1100;
    public const int Worker = 1200;

    public const int Fib = 2001;
    public const int Sort = 2002;
    public const int BusyWait = 2003;

    public const int Io = 3001;
    public const int Cpu = 3002;

    public const int Job = 4001;
}