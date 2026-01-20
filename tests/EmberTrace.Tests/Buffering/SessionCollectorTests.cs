using System.Linq;
using System.Threading.Tasks;
using EmberTrace.Internal.Buffering;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmberTrace.Tests.Buffering;

[TestClass]
public class SessionCollectorTests
{
    [TestMethod]
    public async Task AddChunk_And_RegisterWriter_AreThreadSafe()
    {
        var collector = new SessionCollector();
        var pool = new ChunkPool(8);

        const int chunkTasks = 6;
        const int chunksPerTask = 500;
        const int writerTasks = 4;
        const int writersPerTask = 250;

        var addChunkTasks = Enumerable.Range(0, chunkTasks)
            .Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < chunksPerTask; i++)
                    collector.AddChunk(new Chunk(1));
            }));

        var addWriterTasks = Enumerable.Range(0, writerTasks)
            .Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < writersPerTask; i++)
                {
                    var writer = new ThreadWriter(collector, pool);
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
        var collector = new SessionCollector();
        var pool = new ChunkPool(4);

        collector.AddChunk(new Chunk(1));
        collector.RegisterWriter(new ThreadWriter(collector, pool));

        collector.Clear();

        Assert.IsEmpty(collector.Chunks);
        Assert.IsEmpty(collector.Writers);
        Assert.IsFalse(collector.IsClosed);
    }
}
