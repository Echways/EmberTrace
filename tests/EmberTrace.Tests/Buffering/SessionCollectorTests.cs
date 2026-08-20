using EmberTrace.Internal.Buffering;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Buffering;

[TestClass]
public class SessionCollectorTests
{
    [TestMethod]
    public async Task AddChunk_IsThreadSafe()
    {
        var pool = new ChunkPool(8);
        var options = new SessionOptions
        {
            ChunkCapacity = 8,
            OverflowPolicy = OverflowPolicy.DropNew
        };
        var collector = new SessionCollector(options, pool, options.ChunkCapacity);

        const int chunkTasks = 6;
        const int chunksPerTask = 500;

        var addChunkTasks = Enumerable.Range(0, chunkTasks)
            .Select(_idx => Task.Run(() =>
            {
                for (var i = 0; i < chunksPerTask; i++)
                    collector.TryRentChunk(out _);
            }));

        await Task.WhenAll(addChunkTasks);

        Assert.HasCount(chunkTasks * chunksPerTask, collector.Chunks);
    }

    [TestMethod]
    public void Clear_ResetsCollections()
    {
        var pool = new ChunkPool(4);
        var options = new SessionOptions
        {
            ChunkCapacity = 4,
            OverflowPolicy = OverflowPolicy.DropNew
        };
        var collector = new SessionCollector(options, pool, options.ChunkCapacity);

        collector.TryRentChunk(out _);

        collector.Clear();

        Assert.IsEmpty(collector.Chunks);
        Assert.IsFalse(collector.IsClosed);
    }
}