using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using EmberTrace;
using EmberTrace.Sessions;

namespace EmberTrace.Benchmarks;

[MemoryDiagnoser]
public class ScopeBenchmarks
{
    private const int Operations = 10_000;
    private const int MultiThreadDegree = 4;

    private readonly int _id = Tracer.Id("Bench.Scope");

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
    public void Scope_BeginEnd_MultiThread()
    {
        var options = new ParallelOptions { MaxDegreeOfParallelism = MultiThreadDegree };
        Parallel.For(0, Operations, options, _ =>
        {
            using (Tracer.Scope(_id))
            {
            }
        });
    }
}
