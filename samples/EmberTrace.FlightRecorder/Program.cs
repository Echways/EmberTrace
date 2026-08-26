using EmberTrace;
using EmberTrace.Sessions;

var handle = Tracer.Id("Worker.Handle");
var slow = Tracer.Id("Worker.Slow");

Tracer.Start(new SessionOptions
{
    ChunkCapacity = 8 * 1024,
    OverflowPolicy = OverflowPolicy.DropOldest,
    MaxRetentionWindow = TimeSpan.FromSeconds(2),
    MaxTotalChunks = 64,
    EnableRuntimeMetadata = true,
    RuntimeCounters = RuntimeCounters.Gc | RuntimeCounters.Memory
});

using var stop = new CancellationTokenSource();

var workers = Enumerable.Range(0, 4)
    .Select(index => Task.Run(async () =>
    {
        var random = new Random(index);

        while (!stop.IsCancellationRequested)
        {
            await using (Tracer.ScopeAsync(handle))
            {
                await Task.Delay(random.Next(1, 5), CancellationToken.None);

                if (random.Next(100) < 5)
                    await using (Tracer.ScopeAsync(slow))
                    {
                        await Task.Delay(30, CancellationToken.None);
                    }
            }
        }
    }))
    .ToArray();

await Task.Delay(TimeSpan.FromSeconds(5));

var snapshot = Tracer.Snapshot(TimeSpan.FromSeconds(2));
var path = Path.Combine(Path.GetTempPath(), $"embertrace-flight-{DateTime.UtcNow:yyyyMMdd-HHmmss}.ember");
TraceFormat.Write(snapshot, path);

Console.WriteLine($"Snapshot: {snapshot.EventCount} events over {snapshot.DurationMs:F1} ms");
Console.WriteLine($"Written to: {path}");
Console.WriteLine($"Session still running: {Tracer.IsRunning}");

await Task.Delay(TimeSpan.FromSeconds(1));

var second = Tracer.Snapshot(TimeSpan.FromSeconds(2));
Console.WriteLine($"Second snapshot: {second.EventCount} events, still running: {Tracer.IsRunning}");

stop.Cancel();
await Task.WhenAll(workers);

var final = Tracer.Stop();
Console.WriteLine($"Final session: {final.EventCount} events, dropped {final.DroppedEvents}");
