using System.Linq;
using System.Threading.Tasks;
using EmberTrace.Internal.Buffering;
using EmberTrace.Sessions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmberTrace.Tests.Buffering;

[TestClass]
public class SessionCollectorTests
{
    [TestMethod]
    public async Task AddChunk_And_RegisterWriter_AreThreadSafe()
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
        const int writerTasks = 4;
        const int writersPerTask = 250;

        var addChunkTasks = Enumerable.Range(0, chunkTasks)
            .Select(_idx => Task.Run(() =>
            {
                for (int i = 0; i < chunksPerTask; i++)
                    collector.TryRentChunk(out _);
            }));

        var addWriterTasks = Enumerable.Range(0, writerTasks)
            .Select(_idx => Task.Run(() =>
            {
                for (int i = 0; i < writersPerTask; i++)
                {
                    var writer = new ThreadWriter(collector, default);
                    collector.RegisterWriter(writer);
                }
            }));

        await Task.WhenAll(addChunkTasks.Concat(addWriterTasks));

        var expectedWriterCount = writerTasks * writersPerTask;
        var expectedChunkCount = (chunkTasks * chunksPerTask) + expectedWriterCount;

        Assert.HasCount(expectedWriterCount, collector.Writers);
        Assert.HasCount(expectedChunkCount, collector.Chunks);
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
        collector.RegisterWriter(new ThreadWriter(collector, default));

        collector.Clear();

        Assert.IsEmpty(collector.Chunks);
        Assert.IsEmpty(collector.Writers);
        Assert.IsFalse(collector.IsClosed);
    }
}
