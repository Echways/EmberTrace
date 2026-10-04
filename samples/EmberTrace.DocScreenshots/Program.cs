using EmberTrace;
using EmberTrace.Abstractions.Attributes;
using EmberTrace.DocScreenshots;
using EmberTrace.Flow;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

[assembly: TraceId(Ids.App, "App", "App")]
[assembly: TraceId(Ids.Warmup, "Warmup", "App")]
[assembly: TraceId(Ids.Load, "Load", "App")]
[assembly: TraceId(Ids.Parse, "Parse", "CPU")]
[assembly: TraceId(Ids.Render, "Render", "CPU")]
[assembly: TraceId(Ids.Queue, "Queue", "Workers")]
[assembly: TraceId(Ids.Worker, "Worker", "Workers")]
[assembly: TraceId(Ids.Cpu, "CpuWork", "CPU")]
[assembly: TraceId(Ids.Io, "IoWait", "IO")]
[assembly: TraceId(Ids.Sort, "Sort", "CPU")]
[assembly: TraceId(Ids.Allocate, "Allocate", "Memory")]
[assembly: TraceId(Ids.JobFlow, "JobFlow", "Flow")]

var projectDir = ResolveProjectDir();
var outDir = Path.Combine(projectDir, "out");
Directory.CreateDirectory(outDir);

var scenarios = new List<Scenario>
{
    new("api-tracer-perfetto", "Nested scopes + one flow chain (Tracer API reference).",
        () => RunApiTracerPerfetto(outDir)),
    new("flows-propagation", "Flow propagation across threads/async (Flow concept).",
        () => RunFlowsPropagation(outDir)),
    new("export-opened", "Chrome Trace export to open in Perfetto/SpeedScope (Export guide).",
        () => RunExportOpened(outDir)),
    new("analysis-slice", "Text report with percentiles (Analysis guide).",
        () => RunAnalysisSlice(outDir)),
    new("analysis-flame-graph", "Collapsed stacks for a flame graph (Analysis guide).",
        () => RunAnalysisFlameGraph(outDir)),
    new("usage-instrumentation", "Compiled instrumentation snippet (Usage guide).",
        () => RunUsageInstrumentation(projectDir, outDir)),
    new("getting-started-first-trace", "Minimal first trace + report (Getting started).",
        () => RunGettingStartedFirstTrace(outDir)),
    new("runtime-counters-timeline", "GC, memory and thread pool counters next to scopes (Runtime counters guide).",
        () => RunRuntimeCountersTimeline(outDir)),
    new("generator-generated-code", "Generated metadata provider (Source generator reference).",
        () => CopyGeneratedMetadata(projectDir, outDir)),
    new("auto-instrumentation-generated-code", "Generated [Trace] wrapper (Auto-instrumentation guide).",
        () => RunAutoInstrumentation(projectDir, outDir)),
    new("troubleshooting-common", "Before/after metadata report for troubleshooting.",
        () => RunTroubleshootingCommon(outDir))
};

await RunScenarios(args, scenarios);

static async Task RunScenarios(string[] args, List<Scenario> scenarios)
{
    Console.WriteLine("== EmberTrace.DocScreenshots ==");

    if (args.Any(a => string.Equals(a, "--list", StringComparison.OrdinalIgnoreCase)))
    {
        foreach (var scenario in scenarios)
            Console.WriteLine($"{scenario.Name} - {scenario.Description}");
        return;
    }

    var single = ReadArgValue(args, "--scenario", "-s");
    var runList = string.IsNullOrWhiteSpace(single)
        ? scenarios
        : scenarios.Where(s => string.Equals(s.Name, single, StringComparison.OrdinalIgnoreCase)).ToList();

    if (!runList.Any())
    {
        Console.WriteLine("Unknown scenario: " + single);
        Console.WriteLine("Use --list to see available scenarios.");
        return;
    }

    foreach (var scenario in runList)
    {
        Console.WriteLine();
        Console.WriteLine("== " + scenario.Name + " ==");
        Console.WriteLine(scenario.Description);
        await scenario.Run().ConfigureAwait(false);
    }

    Console.WriteLine();
    Console.WriteLine("ALL OK");
}

static string? ReadArgValue(string[] args, string longName, string shortName)
{
    for (var i = 0; i < args.Length; i++)
        if (string.Equals(args[i], longName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(args[i], shortName, StringComparison.OrdinalIgnoreCase))
            if (i + 1 < args.Length)
                return args[i + 1];

    return null;
}

static Task RunApiTracerPerfetto(string outDir)
{
    Tracer.Start(DefaultSessionOptions());

    using (Tracer.Scope(Ids.App))
    {
        using (Tracer.Scope(Ids.Warmup))
        {
            CpuSpin(160_000);
        }

        using (Tracer.Scope(Ids.Load))
        {
            CpuSpin(120_000);
        }

        var flowId = Tracer.FlowStartNew(Ids.JobFlow);
        using (Tracer.Scope(Ids.Parse))
        {
            SortWork(10_000);
        }

        Tracer.FlowStep(Ids.JobFlow, flowId);
        using (Tracer.Scope(Ids.Render))
        {
            CpuSpin(140_000);
        }

        Tracer.FlowEnd(Ids.JobFlow, flowId);
    }

    var session = Tracer.Stop();
    ExportChromeComplete(session, Path.Combine(outDir, "api-tracer-perfetto.json"));
    return Task.CompletedTask;
}

static async Task RunFlowsPropagation(string outDir)
{
    Tracer.Start(DefaultSessionOptions());

    await using (Tracer.ScopeAsync(Ids.App))
    {
        FlowHandle job;

        using (Tracer.Scope(Ids.Queue))
        {
            job = Tracer.FlowStartNewHandle(Ids.JobFlow);
            CpuSpin(120_000);
        }

        var parse = Task.Run(async () =>
        {
            await using (Tracer.ScopeAsync(Ids.Worker))
            {
                using (Tracer.Scope(Ids.Parse))
                {
                    job.Step();
                    CpuSpin(100_000);
                }

                await Task.Delay(40);
            }
        });

        var load = Task.Run(async () =>
        {
            await using (Tracer.ScopeAsync(Ids.Worker))
            {
                await Task.Delay(30);

                using (Tracer.Scope(Ids.Load))
                {
                    job.Step();
                    SortWork(10_000);
                }
            }
        });

        await Task.WhenAll(parse, load).ConfigureAwait(false);

        using (Tracer.Scope(Ids.Render))
        {
            job.End();
            CpuSpin(110_000);
        }
    }

    var session = Tracer.Stop();
    ExportChromeComplete(session, Path.Combine(outDir, "flows-propagation.json"));
}

static async Task RunExportOpened(string outDir)
{
    Tracer.Start(DefaultSessionOptions());

    using (Tracer.Scope(Ids.App))
    {
        CpuSpin(90_000);
        SortWork(9_000);
    }

    await IoDelay(18);

    var session = Tracer.Stop();
    ExportChromeComplete(session, Path.Combine(outDir, "export-opened.json"));

    var beginEndPath = Path.Combine(outDir, "export-opened-beginend.json");
    using var fs = File.Create(beginEndPath);
    TraceExport.WriteChromeBeginEnd(session, fs, Tracer.CreateMetadata());
    Console.WriteLine("Saved: " + beginEndPath);
}

static async Task RunAnalysisSlice(string outDir)
{
    Tracer.Start(DefaultSessionOptions());

    using (Tracer.Scope(Ids.App))
    {
        for (var i = 0; i < 24; i++)
        {
            CpuSpin(60_000 + i * 8_000);
            SortWork(4_000 + i * 500);
        }
    }

    await IoDelay(25);

    var session = Tracer.Stop();
    var report = TraceText.Write(session.Process(), Tracer.CreateMetadata(), 12, 6, includePercentiles: true);

    WriteText(Path.Combine(outDir, "analysis-slice.txt"), report);
    ExportChromeComplete(session, Path.Combine(outDir, "analysis-slice.json"));
}

static Task RunAnalysisFlameGraph(string outDir)
{
    Tracer.Start(DefaultSessionOptions());

    using (Tracer.Scope(Ids.App))
    {
        using (Tracer.Scope(Ids.Warmup))
        {
            CpuSpin(4_000_000);
        }

        for (var frame = 0; frame < 20; frame++)
        {
            using (Tracer.Scope(Ids.Load))
            {
                using (Tracer.Scope(Ids.Parse))
                {
                    SortWork(4_000);
                    CpuSpin(600_000);
                }

                CpuSpin(400_000);
            }

            using (Tracer.Scope(Ids.Render))
            {
                CpuSpin(900_000);
                SortWork(3_000);
            }
        }
    }

    var session = Tracer.Stop();
    var path = Path.Combine(outDir, "analysis-flame-graph.folded");

    using (var file = File.CreateText(path))
    {
        TraceText.WriteCollapsedStacks(session.Process(), file, Tracer.CreateMetadata());
    }

    Console.WriteLine("Saved: " + path);
    return Task.CompletedTask;
}

static async Task RunUsageInstrumentation(string projectDir, string outDir)
{
    var tracePath = Path.Combine(outDir, "usage-instrumentation.json");
    await UsageInstrumentation.RunAsync(tracePath).ConfigureAwait(false);
    Console.WriteLine("Saved: " + tracePath);

    var snippetPath = Path.Combine(outDir, "usage-instrumentation.cs");
    File.Copy(Path.Combine(projectDir, "Snippets", "UsageInstrumentation.cs"), snippetPath, true);
    Console.WriteLine("Saved: " + snippetPath);
}

static async Task RunGettingStartedFirstTrace(string outDir)
{
    Tracer.Start(DefaultSessionOptions());

    using (Tracer.Scope(Ids.App))
    {
        CpuSpin(70_000);
    }

    await IoDelay(20);

    var session = Tracer.Stop();
    var report = TraceText.Write(session.Process(), Tracer.CreateMetadata(), 8, 4);

    ExportChromeComplete(session, Path.Combine(outDir, "getting-started-first-trace.json"));
    WriteText(Path.Combine(outDir, "getting-started-first-trace.txt"), report);
}

static async Task RunRuntimeCountersTimeline(string outDir)
{
    Tracer.Start(new SessionOptions
    {
        ChunkCapacity = 128 * 1024,
        RuntimeCounters = RuntimeCounters.All,
        RuntimeCounterInterval = TimeSpan.FromMilliseconds(5)
    });

    await using (Tracer.ScopeAsync(Ids.App))
    {
        var workers = Enumerable.Range(0, 3).Select(_ => Task.Run(() =>
        {
            using (Tracer.Scope(Ids.Worker))
            {
                for (var i = 0; i < 10; i++)
                {
                    Allocate(48);
                    SortWork(60_000);
                }
            }
        }));

        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    var session = Tracer.Stop();
    ExportChromeComplete(session, Path.Combine(outDir, "runtime-counters-timeline.json"));
}

static Task CopyGeneratedMetadata(string projectDir, string outDir)
{
    CopyGenerated(projectDir, "EmberTrace.GeneratedTraceMetadataProvider.g.cs",
        Path.Combine(outDir, "generator-generated-code.cs"));
    return Task.CompletedTask;
}

static async Task RunAutoInstrumentation(string projectDir, string outDir)
{
    Tracer.Start(DefaultSessionOptions());

    var service = new OrderService();
    for (var i = 0; i < 20; i++)
        await service.PlaceAsync(i).ConfigureAwait(false);

    var session = Tracer.Stop();
    Console.WriteLine($"Recorded {session.EventCount} events through the generated wrappers.");

    CopyGenerated(projectDir, "EmberTrace.Trace.*OrderService.g.cs",
        Path.Combine(outDir, "auto-instrumentation-generated-code.cs"));
}

static Task RunTroubleshootingCommon(string outDir)
{
    Tracer.Start(DefaultSessionOptions());

    using (Tracer.Scope(Ids.App))
    {
        CpuSpin(80_000);
        SortWork(6_000);
    }

    var session = Tracer.Stop();
    var processed = session.Process();

    var withoutMeta = TraceText.Write(processed, TraceMetadata.FromEntries([]), 6, 4);
    var withMeta = TraceText.Write(processed, Tracer.CreateMetadata(), 6, 4);

    WriteText(Path.Combine(outDir, "troubleshooting-common.txt"),
        "== Without metadata ==" + Environment.NewLine +
        withoutMeta + Environment.NewLine + Environment.NewLine +
        "== With metadata ==" + Environment.NewLine +
        withMeta);
    return Task.CompletedTask;
}

static void CopyGenerated(string projectDir, string pattern, string destPath)
{
    var generatedDir = Path.Combine(projectDir, "obj", "generated");
    var source = Directory.Exists(generatedDir)
        ? Directory.EnumerateFiles(generatedDir, pattern, SearchOption.AllDirectories).FirstOrDefault()
        : null;

    if (source is null)
        throw new FileNotFoundException($"{pattern} not found under {generatedDir}; rebuild the project.");

    File.Copy(source, destPath, true);
    Console.WriteLine("Saved: " + destPath);
}

static void ExportChromeComplete(TraceSession session, string path)
{
    using var fs = File.Create(path);
    TraceExport.WriteChromeComplete(session, fs, Tracer.CreateMetadata());
    Console.WriteLine("Saved: " + path);
}

static void WriteText(string path, string text)
{
    File.WriteAllText(path, text);
    Console.WriteLine("Saved: " + path);
}

static SessionOptions DefaultSessionOptions()
{
    return new SessionOptions { ChunkCapacity = 128 * 1024 };
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
    for (var i = 0; i < n; i++)
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

static void Allocate(int megabytes)
{
    using var _ = Tracer.Scope(Ids.Allocate);

    var blocks = new List<byte[]>(megabytes * 16);
    for (var i = 0; i < megabytes * 16; i++)
        blocks.Add(new byte[64 * 1024]);

    GC.KeepAlive(blocks);
}

static string ResolveProjectDir()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "EmberTrace.DocScreenshots.csproj")))
            return dir.FullName;

    return Directory.GetCurrentDirectory();
}

internal record Scenario(string Name, string Description, Func<Task> Run);

internal static class Ids
{
    public const int App = 1000;
    public const int Warmup = 1010;
    public const int Load = 1020;
    public const int Parse = 1030;
    public const int Render = 1040;

    public const int Queue = 1900;
    public const int Worker = 2000;
    public const int Cpu = 2100;
    public const int Io = 2200;
    public const int Sort = 2300;
    public const int Allocate = 2400;

    public const int JobFlow = 3000;
}
