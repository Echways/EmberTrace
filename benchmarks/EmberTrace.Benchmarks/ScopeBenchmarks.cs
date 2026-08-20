using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using EmberTrace;
using EmberTrace.Sessions;

namespace EmberTrace.Benchmarks;

[MemoryDiagnoser]
public class ScopeBenchmarks
{
    private const int Operations = 10_000;

    private readonly int _id = Tracer.Id("Bench.Scope");

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
        for (int i = 0; i < Operations; i++)
        {
            using (Tracer.Scope(_id))
            {
            }
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
}
