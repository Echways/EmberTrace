using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EmberTrace;
using EmberTrace.Abstractions.Attributes;
using EmberTrace.Flow;
using EmberTrace.ReportText;
using EmberTrace.Sessions;

[assembly: TraceId(Ids.App, "App", "App")]
[assembly: TraceId(Ids.Warmup, "Warmup", "App")]
[assembly: TraceId(Ids.Worker, "Worker", "Workers")]
[assembly: TraceId(Ids.Cpu, "CpuWork", "CPU")]
[assembly: TraceId(Ids.Io, "IoWait", "IO")]
[assembly: TraceId(Ids.Sort, "Sort", "CPU")]
[assembly: TraceId(Ids.AsyncBlock, "AsyncBlock", "Async")]
[assembly: TraceId(Ids.JobFlow, "JobFlow", "Flow")]
[assembly: TraceId(Ids.MarkStandalone, "MarkedStandalone", "Marked")]
[assembly: TraceId(Ids.MarkSlice, "MarkedSlice", "Marked")]
[assembly: TraceId(Ids.MarkAsync, "MarkedAsync", "Marked")]
[assembly: TraceId(Ids.AfterSlice, "AfterSlice", "App")]

static void EnsureDir(string path)
{
    var dir = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(dir))
        Directory.CreateDirectory(dir);
}

static void CpuSpin(int iters)
{
    using var _ = Tracer.Scope(Ids.Cpu);

    var x = 1;
    for (var i = 0; i < iters; i++)
        x = unchecked(x * 1664525 + 1013904223);

    if (x == 42)
        Console.WriteLine(x);
}

static async Task IoDelay(int ms)
{
    await using var _ = Tracer.ScopeAsync(Ids.Io);
    await Task.Delay(ms).ConfigureAwait(false);
}

static int[] MakeData(int n)
{
    var rnd = new Random(123);
    var a = new int[n];
    for (int i = 0; i < n; i++)
        a[i] = rnd.Next();
    return a;
}

static void SortWork(int n)
{
    using var _ = Tracer.Scope(Ids.Sort);
    var a = MakeData(n);
    Array.Sort(a);
    if (a[0] == int.MinValue)
        Console.WriteLine(a[0]);
}

static async Task WorkerAsync(int workerId, int loops, FlowHandle flow)
{
    await using (Tracer.ScopeAsync(Ids.Worker))
    {
        for (int i = 0; i < loops; i++)
        {
            CpuSpin(90_000 + workerId * 10_000);
            flow.Step();
            await IoDelay(10 + workerId * 5).ConfigureAwait(false);
            flow.Step();
            SortWork(18_000);
        }
    }
}

static void ExportFull(TraceSession session, string completePath, string beginEndPath)
{
    EnsureDir(completePath);
    EnsureDir(beginEndPath);

    var meta = Tracer.CreateMetadata();

    using (var fs = File.Create(completePath))
        TraceExport.WriteChromeComplete(session, fs, meta: meta);

    using (var fs = File.Create(beginEndPath))
        TraceExport.WriteChromeBeginEnd(session, fs, meta: meta);
}

Directory.CreateDirectory("out");

Console.WriteLine("== EmberTrace.Demo3 ==");

Console.WriteLine("1) MarkedComplete (not running)");
var standalonePath = Path.Combine("out", "marked_standalone.json");
TraceExport.MarkedComplete(
    name: "MarkedStandalone",
    outputPath: standalonePath,
    body: () =>
    {
        using var app = Tracer.Scope(Ids.App);
        using var warm = Tracer.Scope(Ids.Warmup);

        CpuSpin(200_000);
        SortWork(25_000);
    });

Console.WriteLine("Saved: " + standalonePath);

Console.WriteLine("2) Full session: scopes + flows + async + export + analysis");
var options = new SessionOptions
{
    ChunkCapacity = 128 * 1024,
    OverflowPolicy = OverflowPolicy.Drop
};

Tracer.Start(options);

await using (Tracer.ScopeAsync(Ids.App))
{
    using (Tracer.Scope(Ids.Warmup))
    {
        Thread.SpinWait(200_000);
        SortWork(8_000);
    }

    var flow = Tracer.FlowStartNewHandle(Ids.JobFlow);

    var t1 = Task.Run(() => WorkerAsync(1, 2, flow));
    var t2 = Task.Run(() => WorkerAsync(2, 2, flow));
    await Task.WhenAll(t1, t2);

    flow.End();

    var flowId = Tracer.FlowStartNew(Ids.JobFlow);
    CpuSpin(150_000);
    Tracer.FlowStep(Ids.JobFlow, flowId);
    SortWork(12_000);
    Tracer.FlowEnd(Ids.JobFlow, flowId);

    await using (Tracer.ScopeAsync(Ids.AsyncBlock))
        await Task.Delay(20);
}

var session = Tracer.Stop();
var processed = session.Process();
var meta = Tracer.CreateMetadata();

Console.WriteLine(TraceText.Write(processed, meta: meta, topHotspots: 20, maxDepth: 8));

var fullComplete = Path.Combine("out", "full_complete.json");
var fullBeginEnd = Path.Combine("out", "full_beginend.json");
ExportFull(session, fullComplete, fullBeginEnd);

Console.WriteLine("Saved: " + fullComplete);
Console.WriteLine("Saved: " + fullBeginEnd);
Console.WriteLine("Open in chrome://tracing");

Console.WriteLine("3) MarkedCompleteAsync (not running)");
var markedAsyncPath = Path.Combine("out", "marked_async.json");
await TraceExport.MarkedCompleteAsync(
    name: "MarkedAsync",
    outputPath: markedAsyncPath,
    body: async () =>
    {
        await using (Tracer.ScopeAsync(Ids.MarkAsync))
            await Task.Delay(30);
    });

Console.WriteLine("Saved: " + markedAsyncPath);

Console.WriteLine("4) SliceAndResume");
Tracer.Start(options);

using (Tracer.Scope(Ids.App))
{
    CpuSpin(120_000);
    SortWork(10_000);
}

var slicePath = Path.Combine("out", "marked_slice.json");
var sliceResult = TraceExport.MarkedCompleteEx(
    name: "MarkedSlice",
    outputPath: slicePath,
    body: () =>
    {
        using var app = Tracer.Scope(Ids.App);
        CpuSpin(220_000);
        SortWork(20_000);
    },
    running: MarkedRunningSessionMode.SliceAndResume,
    resumeOptions: options);

var sliceFull = Path.Combine("out", "marked_slice_full.json");
sliceResult.SaveFullChromeComplete(sliceFull, meta: meta);

Console.WriteLine("Saved: " + slicePath);
Console.WriteLine("Saved: " + sliceFull);
Console.WriteLine("Open in chrome://tracing");

using (Tracer.Scope(Ids.AfterSlice))
{
    CpuSpin(140_000);
    SortWork(12_000);
}

var resumed = Tracer.Stop();

var resumedComplete = Path.Combine("out", "resumed_complete.json");
var resumedBeginEnd = Path.Combine("out", "resumed_beginend.json");
ExportFull(resumed, resumedComplete, resumedBeginEnd);

Console.WriteLine("Saved: " + resumedComplete);
Console.WriteLine("Saved: " + resumedBeginEnd);
Console.WriteLine("Open in chrome://tracing");

Console.WriteLine("ALL OK");

static class Ids
{
    public const int App = 1000;
    public const int Warmup = 1010;
    public const int Worker = 2000;
    public const int Cpu = 2100;
    public const int Io = 2200;
    public const int Sort = 2300;
    public const int AsyncBlock = 2400;

    public const int JobFlow = 3000;

    public const int MarkStandalone = 9001;
    public const int MarkSlice = 9002;
    public const int MarkAsync = 9003;

    public const int AfterSlice = 9100;
}
