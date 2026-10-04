using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using EmberTrace.Sessions;

namespace EmberTrace.Benchmarks;

[MemoryDiagnoser]
public class HotPathBenchmarks
{
    private readonly int _id = Tracer.Id("Bench.HotPath");

    private readonly TracedService _service = new();

    [GlobalSetup]
    public void Start()
    {
        Tracer.Start(new SessionOptions
        {
            MaxTotalChunks = 8,
            OverflowPolicy = OverflowPolicy.DropOldest
        });
    }

    [GlobalCleanup]
    public void Stop()
    {
        Tracer.Stop();
    }

    [Benchmark(Baseline = true)]
    public long Clock_TwoTimestamps()
    {
        return Stopwatch.GetTimestamp() + Stopwatch.GetTimestamp();
    }

    [Benchmark]
    public void Scope_BeginEnd()
    {
        using (Tracer.Scope(_id))
        {
        }
    }

    [Benchmark]
    public void Instant()
    {
        Tracer.Instant(_id);
    }

    [Benchmark]
    public async Task ScopeAsync_BeginEnd()
    {
        await using (Tracer.ScopeAsync(_id))
        {
        }
    }

    [Benchmark]
    public ValueTask<int> Trace_Async()
    {
        return _service.WorkAsync(1);
    }
}
