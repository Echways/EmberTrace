using BenchmarkDotNet.Attributes;
using EmberTrace.Sessions;

namespace EmberTrace.Benchmarks;

[MemoryDiagnoser]
public class ScopeBenchmarks
{
    private const int Operations = 10_000;

    private readonly int _id = Tracer.Id("Bench.Scope");

    private readonly int _manualId = Tracer.Id("TracedService.Work");

    private readonly TracedService _service = new();

    public static IEnumerable<int> ThreadCounts()
    {
        yield return 4;
        if (Environment.ProcessorCount > 4)
            yield return Environment.ProcessorCount;
    }

    [IterationSetup]
    public void Setup()
    {
        Tracer.Start(new SessionOptions
        {
            ChunkCapacity = 1024,
            OverflowPolicy = OverflowPolicy.DropNew
        });
    }

    [IterationCleanup]
    public void Cleanup()
    {
        Tracer.Stop();
    }

    [Benchmark]
    public void Scope_BeginEnd_SingleThread()
    {
        for (var i = 0; i < Operations; i++)
            using (Tracer.Scope(_id))
            {
            }
    }

    [Benchmark]
    [ArgumentsSource(nameof(ThreadCounts))]
    public void Scope_BeginEnd_MultiThread(int threads)
    {
        var options = new ParallelOptions { MaxDegreeOfParallelism = threads };
        Parallel.For(0, Operations, options, _ =>
        {
            using (Tracer.Scope(_id))
            {
            }
        });
    }

    [Benchmark]
    public int Trace_Sync_SessionRunning()
    {
        var total = 0;
        for (var i = 0; i < Operations; i++)
            total += _service.Work(i);

        return total;
    }

    [Benchmark]
    public int Manual_Sync_SessionRunning()
    {
        var total = 0;
        for (var i = 0; i < Operations; i++)
            using (Tracer.Scope(_manualId))
            {
                total += i + 1;
            }

        return total;
    }

    [Benchmark]
    public async Task<int> Trace_Async_SessionRunning()
    {
        var total = 0;
        for (var i = 0; i < Operations; i++)
            total += await _service.WorkAsync(i).ConfigureAwait(false);

        return total;
    }
}

[MemoryDiagnoser]
public class TraceOverheadBenchmarks
{
    private const int Operations = 10_000;

    private readonly TracedService _service = new();

    [Benchmark]
    public int Trace_Sync_SessionStopped()
    {
        var total = 0;
        for (var i = 0; i < Operations; i++)
            total += _service.Work(i);

        return total;
    }

    [Benchmark]
    public async Task<int> Trace_Async_SessionStopped()
    {
        var total = 0;
        for (var i = 0; i < Operations; i++)
            total += await _service.WorkAsync(i).ConfigureAwait(false);

        return total;
    }
}
