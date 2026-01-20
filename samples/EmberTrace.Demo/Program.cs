using System;
using System.IO;
using System.Linq;
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
[assembly: TraceId(Ids.JobFlow, "JobFlow", "Flow")]
[assembly: TraceId(Ids.MarkStandalone, "MarkedStandalone", "Marked")]
[assembly: TraceId(Ids.MarkSlice, "MarkedSlice", "Marked")]
[assembly: TraceId(Ids.AfterSlice, "AfterSlice", "App")]

static void EnsureDir(string path)
{
    var dir = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(dir))
        Directory.CreateDirectory(dir);
}

static void Assert(bool ok, string message)
{
    if (!ok) throw new Exception("ASSERT FAILED: " + message);
}

static void AssertFileLooksOk(string path, params string[] mustContain)
{
    Assert(File.Exists(path), $"file not found: {path}");
    var text = File.ReadAllText(path);
    Assert(text.Length > 50, $"file too small: {path}");
    foreach (var s in mustContain)
        Assert(text.Contains(s, StringComparison.Ordinal), $"file {path} does not contain '{s}'");
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

static async Task Worker(int workerId, int loops, FlowHandle flow)
{
    await using (Tracer.ScopeAsync(Ids.Worker))
    {
        for (int i = 0; i < loops; i++)
        {
            CpuSpin(120_000 + workerId * 10_000);
            flow.Step();
            await IoDelay(10 + workerId * 5).ConfigureAwait(false);
            flow.Step();
            SortWork(20_000);
        }
    }
}

static void VerifyMetadata()
{
    var meta = Tracer.CreateMetadata();

    Assert(meta.TryGet(Ids.App, out var app), "metadata missing for App");
    Assert(app.Name == "App", "App name mismatch");
    Assert(app.Category == "App", "App category mismatch");

    Assert(meta.TryGet(Ids.JobFlow, out var flow), "metadata missing for JobFlow");
    Assert(flow.Name == "JobFlow", "JobFlow name mismatch");
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

static void PrintTextReport(TraceSession session, string title)
{
    var processed = session.Process();
    var meta = Tracer.CreateMetadata();

    Console.WriteLine();
    Console.WriteLine("==== " + title + " ====");
    Console.WriteLine(TraceText.Write(processed, meta: meta, topHotspots: 20, maxDepth: 10));
}

Directory.CreateDirectory("out");

Console.WriteLine("== EmberTrace.UtilityTest ==");

VerifyMetadata();

Console.WriteLine();
Console.WriteLine("1) Standalone MarkedComplete (not running)");

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

AssertFileLooksOk(standalonePath, "MarkedStandalone", "\"traceEvents\"");

Console.WriteLine("OK: " + standalonePath);

Console.WriteLine();
Console.WriteLine("2) Regular session: scopes + flows + async + multithread");

var options = new SessionOptions
{
    ChunkCapacity = 128 * 1024,
    OverflowPolicy = OverflowPolicy.Drop
};

Tracer.Start(options);

using (Tracer.Scope(Ids.App))
{
    using (Tracer.Scope(Ids.Warmup))
        CpuSpin(150_000);

    var flow = Tracer.FlowStartNewHandle(Ids.JobFlow);

    var t1 = Task.Run(() => Worker(1, 2, flow));
    var t2 = Task.Run(() => Worker(2, 2, flow));

    Task.WaitAll(t1, t2);

    flow.End();

    var flowId = Tracer.NewFlowId();
    Tracer.FlowStart(Ids.JobFlow, flowId);
    CpuSpin(180_000);
    Tracer.FlowStep(Ids.JobFlow, flowId);
    SortWork(18_000);
    Tracer.FlowEnd(Ids.JobFlow, flowId);
}

var session = Tracer.Stop();

var fullComplete = Path.Combine("out", "full_complete.json");
var fullBeginEnd = Path.Combine("out", "full_beginend.json");
ExportFull(session, fullComplete, fullBeginEnd);

AssertFileLooksOk(fullComplete, "\"traceEvents\"", "JobFlow");
AssertFileLooksOk(fullBeginEnd, "\"traceEvents\"", "JobFlow");

Console.WriteLine("OK: " + fullComplete);
Console.WriteLine("OK: " + fullBeginEnd);

PrintTextReport(session, "Full session report");

Console.WriteLine();
Console.WriteLine("3) SliceAndResume (already running): export only marked window, then continue session");

Tracer.Start(options);

using (Tracer.Scope(Ids.App))
{
    CpuSpin(120_000);
    SortWork(12_000);
}

var slicePath = Path.Combine("out", "marked_slice.json");

TraceExport.MarkedComplete(
    name: "MarkedSlice",
    outputPath: slicePath,
    body: () =>
    {
        using var app = Tracer.Scope(Ids.App);

        CpuSpin(250_000);
        IoDelay(30).GetAwaiter().GetResult();
        SortWork(22_000);
    },
    running: MarkedRunningSessionMode.SliceAndResume,
    resumeOptions: options);

AssertFileLooksOk(slicePath, "MarkedSlice", "\"traceEvents\"");

using (Tracer.Scope(Ids.AfterSlice))
{
    CpuSpin(140_000);
    IoDelay(20).GetAwaiter().GetResult();
}

var resumed = Tracer.Stop();

var resumedComplete = Path.Combine("out", "resumed_complete.json");
var resumedBeginEnd = Path.Combine("out", "resumed_beginend.json");
ExportFull(resumed, resumedComplete, resumedBeginEnd);

AssertFileLooksOk(resumedComplete, "\"traceEvents\"", "AfterSlice");
AssertFileLooksOk(resumedBeginEnd, "\"traceEvents\"", "AfterSlice");

Console.WriteLine("OK: " + slicePath);
Console.WriteLine("OK: " + resumedComplete);
Console.WriteLine("OK: " + resumedBeginEnd);

PrintTextReport(resumed, "Resumed session report");

Console.WriteLine();
Console.WriteLine("ALL OK.");

static class Ids
{
    public const int App = 1000;
    public const int Warmup = 1010;

    public const int Worker = 2000;
    public const int Cpu = 2100;
    public const int Io = 2200;
    public const int Sort = 2300;

    public const int JobFlow = 3000;

    public const int MarkStandalone = 9001;
    public const int MarkSlice = 9002;

    public const int AfterSlice = 9100;
}