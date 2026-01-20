using System;
using System.IO;
using System.Threading.Tasks;
using EmberTrace;
using EmberTrace.ReportText;
using EmberTrace.Sessions;

static void Busy(int spins)
{
    using var s = Tracer.Scope(Ids.Busy);
    var x = 0;
    for (var i = 0; i < spins; i++)
        x = (x * 1664525) + 1013904223;
    if (x == 42) Console.WriteLine(x);
}

static async Task SimulatedIoAsync(int ms)
{
    await using var s = Tracer.ScopeAsync(Ids.Io);
    await Task.Delay(ms).ConfigureAwait(false);
}

static void WriteTextReport(string title, TraceSession session)
{
    var processed = session.Process();
    var meta = Tracer.CreateMetadata();
    var text = TraceText.Write(processed, meta: meta, topHotspots: 20, maxDepth: 8);
    Console.WriteLine();
    Console.WriteLine("==== " + title + " ====");
    Console.WriteLine(text);
}

static void EnsureOutDir()
{
    Directory.CreateDirectory("out");
}

EnsureOutDir();

Console.WriteLine("1) Standalone MarkedComplete (session not running)");
TraceExport.MarkedComplete(
    name: "StandaloneBlock",
    outputPath: Path.Combine("out", "standalone.json"),
    body: () =>
    {
        using var app = Tracer.Scope(Ids.App);
        using var warm = Tracer.Scope(Ids.Warmup);
        Busy(300_000);
        Busy(200_000);
    });

Console.WriteLine("Saved: out/standalone.json");

Console.WriteLine();
Console.WriteLine("2) SliceAndResume while a session is already running (sync + async inside)");

var resumeOpts = new SessionOptions
{
    ChunkCapacity = 64 * 1024,
    OverflowPolicy = OverflowPolicy.Drop
};

Tracer.Start(resumeOpts);

using (Tracer.Scope(Ids.RunningOuter))
{
    Busy(250_000);
    Busy(150_000);
}

try
{
    TraceExport.MarkedComplete(
        name: "SliceBlock",
        outputPath: Path.Combine("out", "slice.json"),
        body: () =>
        {
            using var app = Tracer.Scope(Ids.App);
            Busy(400_000);
            SimulatedIoAsync(30).GetAwaiter().GetResult();
            Busy(200_000);
        },
        running: MarkedRunningSessionMode.SliceAndResume,
        resumeOptions: resumeOpts);

    Console.WriteLine("Saved: out/slice.json");
}
catch (Exception ex)
{
    Console.WriteLine("SliceBlock threw: " + ex.GetType().Name + " - " + ex.Message);
}

using (Tracer.Scope(Ids.AfterSlice))
{
    Busy(220_000);
    SimulatedIoAsync(20).GetAwaiter().GetResult();
    Busy(120_000);
}

var resumedSession = Tracer.Stop();

WriteTextReport("Resumed session report (after SliceAndResume)", resumedSession);

var resumedJson = Path.Combine("out", "resumed_full.json");
using (var fs = File.Create(resumedJson))
{
    var meta = Tracer.CreateMetadata();
    TraceExport.WriteChromeComplete(resumedSession, fs, meta: meta);
}

Console.WriteLine("Saved: out/resumed_full.json");

Console.WriteLine();
Console.WriteLine("Done.");

static class Ids
{
    public static readonly int App = Tracer.Id("App");
    public static readonly int Warmup = Tracer.Id("Warmup");
    public static readonly int Busy = Tracer.Id("Busy");
    public static readonly int Io = Tracer.Id("Io");
    public static readonly int RunningOuter = Tracer.Id("RunningOuter");
    public static readonly int AfterSlice = Tracer.Id("AfterSlice");
}