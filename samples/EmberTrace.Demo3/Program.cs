using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EmberTrace;
using EmberTrace.Abstractions.Attributes;
using EmberTrace.Flow;
using EmberTrace.Metadata;
using EmberTrace.ReportText;
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
[assembly: TraceId(Ids.AsyncBlock, "AsyncBlock", "Async")]
[assembly: TraceId(Ids.JobFlow, "JobFlow", "Flow")]

var outDir = ResolveOutDir();
var projectDir = ResolveProjectDir();
Directory.CreateDirectory(outDir);

var scenarios = new List<Scenario>
{
    new("api-tracer-perfetto", "Nested scopes + one flow chain (Tracer API reference).",
        () => RunApiTracerPerfetto(outDir)),
    new("flows-propagation", "Flow propagation across threads/async (Flow concept).",
        () => RunFlowsPropagation(outDir)),
    new("export-opened", "Chrome Trace export to open in Perfetto/SpeedScope (Export guide).",
        () => RunExportOpened(outDir)),
    new("analysis-slice", "Text report for analysis screenshot (Analysis guide).",
        () => RunAnalysisSlice(outDir)),
    new("usage-instrumentation", "Code snippet for usage screenshot (Usage guide).",
        () => WriteUsageSnippet(outDir)),
    new("getting-started-first-trace", "Minimal first trace + report (Getting started).",
        () => RunGettingStartedFirstTrace(outDir)),
    new("generator-generated-code", "Copy generator output from obj for screenshot.",
        () => CopyGeneratorOutput(projectDir, outDir)),
    new("troubleshooting-common", "Before/after metadata report for troubleshooting.",
        () => RunTroubleshootingCommon(outDir))
};

await RunScenarios(args, scenarios);

static async Task RunScenarios(string[] args, List<Scenario> scenarios)
{
    Console.WriteLine("== EmberTrace.Demo3 ==");

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
    {
        if (string.Equals(args[i], longName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(args[i], shortName, StringComparison.OrdinalIgnoreCase))
        {
            if (i + 1 < args.Length)
                return args[i + 1];
        }
    }

    return null;
}

static Task RunApiTracerPerfetto(string outDir)
{
    var options = DefaultSessionOptions();
    Tracer.Start(options);

    using (Tracer.Scope(Ids.App))
    {
        using (Tracer.Scope(Ids.Warmup))
            CpuSpin(160_000);

        using (Tracer.Scope(Ids.Load))
            CpuSpin(120_000);

        var flowId = Tracer.FlowStartNew(Ids.JobFlow);
        using (Tracer.Scope(Ids.Parse))
            SortWork(10_000);
        Tracer.FlowStep(Ids.JobFlow, flowId);
        using (Tracer.Scope(Ids.Render))
            CpuSpin(140_000);
        Tracer.FlowEnd(Ids.JobFlow, flowId);
    }

    var session = Tracer.Stop();
    var meta = Tracer.CreateMetadata();
    var path = Path.Combine(outDir, "api-tracer-perfetto.json");
    ExportChromeComplete(session, path, meta);
    Console.WriteLine("Saved: " + path);
    return Task.CompletedTask;
}

static async Task RunFlowsPropagation(string outDir)
{
    var options = DefaultSessionOptions();
    Tracer.Start(options);

    await using (Tracer.ScopeAsync(Ids.App))
    {
        var flowId = Tracer.FlowStartNew(Ids.JobFlow);

        using (Tracer.Scope(Ids.Queue))
        {
            Tracer.FlowStep(Ids.JobFlow, flowId);
            CpuSpin(120_000);
        }

        var t1 = Task.Run(async () =>
        {
            await using (Tracer.ScopeAsync(Ids.Worker))
            {
                Tracer.FlowStep(Ids.JobFlow, flowId);
                CpuSpin(100_000);
                await Task.Delay(40);
                Tracer.FlowStep(Ids.JobFlow, flowId);
            }
        });

        var t2 = Task.Run(async () =>
        {
            await using (Tracer.ScopeAsync(Ids.Worker))
            {
                await Task.Delay(30);
                Tracer.FlowStep(Ids.JobFlow, flowId);
                SortWork(10_000);
            }
        });

        await Task.WhenAll(t1, t2).ConfigureAwait(false);

        using (Tracer.Scope(Ids.Render))
        {
            Tracer.FlowEnd(Ids.JobFlow, flowId);
            CpuSpin(110_000);
        }
    }

    var session = Tracer.Stop();
    var meta = Tracer.CreateMetadata();
    var path = Path.Combine(outDir, "flows-propagation.json");
    ExportChromeComplete(session, path, meta);
    Console.WriteLine("Saved: " + path);
}

static async Task RunExportOpened(string outDir)
{
    var options = DefaultSessionOptions();
    Tracer.Start(options);

    using (Tracer.Scope(Ids.App))
    {
        CpuSpin(90_000);
        SortWork(9_000);
    }

    await IoDelay(18);

    var session = Tracer.Stop();
    var meta = Tracer.CreateMetadata();

    var completePath = Path.Combine(outDir, "export-opened.json");
    var beginEndPath = Path.Combine(outDir, "export-opened-beginend.json");
    ExportChromeComplete(session, completePath, meta);
    ExportChromeBeginEnd(session, beginEndPath, meta);

    Console.WriteLine("Saved: " + completePath);
    Console.WriteLine("Saved: " + beginEndPath);
}

static async Task RunAnalysisSlice(string outDir)
{
    var options = DefaultSessionOptions();
    Tracer.Start(options);

    using (Tracer.Scope(Ids.App))
    {
        for (var i = 0; i < 3; i++)
        {
            CpuSpin(140_000);
            SortWork(12_000);
        }
    }

    await IoDelay(25);

    var session = Tracer.Stop();
    var processed = session.Process();
    var meta = Tracer.CreateMetadata();

    var reportText = TraceText.Write(processed, meta: meta, topHotspots: 12, maxDepth: 6);
    var reportPath = Path.Combine(outDir, "analysis-slice.txt");
    File.WriteAllText(reportPath, reportText);

    var tracePath = Path.Combine(outDir, "analysis-slice.json");
    ExportChromeComplete(session, tracePath, meta);

    Console.WriteLine("Saved: " + reportPath);
    Console.WriteLine("Saved: " + tracePath);
}

static Task WriteUsageSnippet(string outDir)
{
    var snippetPath = Path.Combine(outDir, "usage-instrumentation.cs");
    var lines = new[]
    {
        "using System.IO;",
        "using System.Threading.Tasks;",
        "using EmberTrace;",
        "using EmberTrace.Abstractions.Attributes;",
        "",
        "[assembly: TraceId(1000, \"App\", \"App\")]",
        "[assembly: TraceId(2100, \"IoWait\", \"IO\")]",
        "",
        "static async Task RunAsync()",
        "{",
        "    Tracer.Start();",
        "",
        "    using (Tracer.Scope(1000))",
        "    {",
        "        await using (Tracer.ScopeAsync(2100))",
        "            await Task.Delay(10);",
        "    }",
        "",
        "    var session = Tracer.Stop();",
        "    var meta = Tracer.CreateMetadata();",
        "",
        "    Directory.CreateDirectory(\"out\");",
        "    using var fs = File.Create(\"out/trace.json\");",
        "    TraceExport.WriteChromeComplete(session, fs, meta: meta);",
        "}",
        "",
        "await RunAsync();"
    };
    File.WriteAllText(snippetPath, string.Join(Environment.NewLine, lines));
    Console.WriteLine("Saved: " + snippetPath);
    return Task.CompletedTask;
}

static async Task RunGettingStartedFirstTrace(string outDir)
{
    var options = DefaultSessionOptions();
    Tracer.Start(options);

    using (Tracer.Scope(Ids.App))
        CpuSpin(70_000);

    await IoDelay(20);

    var session = Tracer.Stop();
    var processed = session.Process();
    var meta = Tracer.CreateMetadata();

    var tracePath = Path.Combine(outDir, "getting-started-first-trace.json");
    ExportChromeComplete(session, tracePath, meta);

    var reportPath = Path.Combine(outDir, "getting-started-first-trace.txt");
    var reportText = TraceText.Write(processed, meta: meta, topHotspots: 8, maxDepth: 4);
    File.WriteAllText(reportPath, reportText);

    Console.WriteLine("Saved: " + tracePath);
    Console.WriteLine("Saved: " + reportPath);
}

static Task CopyGeneratorOutput(string projectDir, string outDir)
{
    if (string.IsNullOrWhiteSpace(projectDir))
    {
        Console.WriteLine("Project directory not found; cannot locate generator output.");
        return Task.CompletedTask;
    }

    var objDir = Path.Combine(projectDir, "obj");
    if (!Directory.Exists(objDir))
    {
        Console.WriteLine("obj/ not found; build the project first.");
        return Task.CompletedTask;
    }

    string? sourcePath = null;

    var exactName = "EmberTrace.GeneratedTraceMetadataProvider.g.cs";
    foreach (var file in Directory.EnumerateFiles(objDir, exactName, SearchOption.AllDirectories))
    {
        if (LooksLikeGeneratorOutput(file))
        {
            sourcePath = file;
            break;
        }
    }

    if (sourcePath == null)
    {
        foreach (var file in Directory.EnumerateFiles(objDir, "*.g.cs", SearchOption.AllDirectories))
        {
            if (LooksLikeGeneratorOutput(file))
            {
                sourcePath = file;
                break;
            }
        }
    }

    if (sourcePath == null)
    {
        Console.WriteLine("Generator output not found; rebuild with -p:EmitCompilerGeneratedFiles=true.");
        return Task.CompletedTask;
    }

    var destPath = Path.Combine(outDir, "generator-generated-code.cs");
    File.Copy(sourcePath, destPath, overwrite: true);
    Console.WriteLine("Saved: " + destPath);
    return Task.CompletedTask;
}

static Task RunTroubleshootingCommon(string outDir)
{
    var options = DefaultSessionOptions();
    Tracer.Start(options);

    using (Tracer.Scope(Ids.App))
    {
        CpuSpin(80_000);
        SortWork(6_000);
    }

    var session = Tracer.Stop();
    var processed = session.Process();

    var withoutMeta = TraceText.Write(processed, meta: null, topHotspots: 6, maxDepth: 4);
    var withMeta = TraceText.Write(processed, meta: Tracer.CreateMetadata(), topHotspots: 6, maxDepth: 4);

    var path = Path.Combine(outDir, "troubleshooting-common.txt");
    var text = "== Without metadata ==" + Environment.NewLine +
               withoutMeta + Environment.NewLine + Environment.NewLine +
               "== With metadata ==" + Environment.NewLine +
               withMeta;
    File.WriteAllText(path, text);
    Console.WriteLine("Saved: " + path);
    return Task.CompletedTask;
}

static bool LooksLikeGeneratorOutput(string path)
{
    const int maxChars = 8000;
    using var reader = new StreamReader(path);
    var buffer = new char[maxChars];
    var read = reader.ReadBlock(buffer, 0, buffer.Length);
    var text = new string(buffer, 0, read);
    return text.Contains("GeneratedTraceMetadataProvider", StringComparison.Ordinal) &&
           text.Contains("TraceMetadata.Register", StringComparison.Ordinal);
}

static void ExportChromeComplete(TraceSession session, string path, ITraceMetadataProvider meta)
{
    EnsureDir(path);
    using var fs = File.Create(path);
    TraceExport.WriteChromeComplete(session, fs, meta: meta);
}

static void ExportChromeBeginEnd(TraceSession session, string path, ITraceMetadataProvider meta)
{
    EnsureDir(path);
    using var fs = File.Create(path);
    TraceExport.WriteChromeBeginEnd(session, fs, meta: meta);
}

static SessionOptions DefaultSessionOptions()
{
    return new SessionOptions
    {
        ChunkCapacity = 128 * 1024,
        OverflowPolicy = OverflowPolicy.DropNew
    };
}

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

static string ResolveOutDir()
{
    var cwd = Directory.GetCurrentDirectory();
    if (File.Exists(Path.Combine(cwd, "EmberTrace.slnx")))
        return Path.Combine(cwd, "samples", "EmberTrace.Demo3", "out");
    if (File.Exists(Path.Combine(cwd, "EmberTraceDemo3.csproj")))
        return Path.Combine(cwd, "out");
    return Path.Combine(cwd, "out");
}

static string ResolveProjectDir()
{
    var baseDir = AppContext.BaseDirectory;
    var probe = FindUpwards(baseDir, "EmberTraceDemo3.csproj");
    if (!string.IsNullOrWhiteSpace(probe))
        return Path.GetDirectoryName(probe) ?? string.Empty;

    var cwd = Directory.GetCurrentDirectory();
    probe = FindUpwards(cwd, "EmberTraceDemo3.csproj");
    if (!string.IsNullOrWhiteSpace(probe))
        return Path.GetDirectoryName(probe) ?? string.Empty;

    var repoPath = Path.Combine(cwd, "samples", "EmberTrace.Demo3", "EmberTraceDemo3.csproj");
    if (File.Exists(repoPath))
        return Path.GetDirectoryName(repoPath) ?? string.Empty;

    return string.Empty;
}

static string FindUpwards(string startDir, string fileName)
{
    var current = new DirectoryInfo(startDir);
    while (current is not null)
    {
        var candidate = Path.Combine(current.FullName, fileName);
        if (File.Exists(candidate))
            return candidate;
        current = current.Parent;
    }

    return string.Empty;
}

record Scenario(string Name, string Description, Func<Task> Run);

static class Ids
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
    public const int AsyncBlock = 2400;

    public const int JobFlow = 3000;
}
