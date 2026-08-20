using EmberTrace.Internal.Buffering;

namespace EmberTrace.Tests.Buffering;

[TestClass]
public class ChunkPoolTests
{
    [TestMethod]
    public void ReturnAndRent_MultiThreaded_PreservesAllChunks()
    {
        var pool = new ChunkPool(8);
        var chunks = Enumerable.Range(0, 1000).Select(_ => new Chunk(8)).ToArray();

        Parallel.For(0, chunks.Length, i => pool.Return(chunks[i]));

        var rented = new HashSet<Chunk>();
        for (var i = 0; i < chunks.Length; i++)
            rented.Add(pool.Rent());

        Assert.HasCount(chunks.Length, rented);

        var originals = new HashSet<Chunk>(chunks);
        var reused = rented.Count(c => originals.Contains(c));
        Assert.AreEqual(chunks.Length, reused);
    }

    [TestMethod]
    public void RentAndReturn_UnderConcurrency_NeverHandsOneChunkToTwoThreads()
    {
        var pool = new ChunkPool(4);
        for (var i = 0; i < 32; i++)
            pool.Return(new Chunk(4));

        var collisions = 0;

        Parallel.For(0, Environment.ProcessorCount * 2, _ =>
        {
            var marker = Environment.CurrentManagedThreadId;
            for (var i = 0; i < 100_000; i++)
            {
                var chunk = pool.Rent();
                chunk.Count = marker;
                Thread.SpinWait(10);
                if (Volatile.Read(ref chunk.Count) != marker)
                    Interlocked.Increment(ref collisions);
                pool.Return(chunk);
            }
        });

        Assert.AreEqual(0, collisions, "a pooled chunk was handed to two threads at once");
    }

    [TestMethod]
    public void Rent_WhenPoolIsEmpty_ReturnsNewChunk()
    {
        var pool = new ChunkPool(4);

        var chunk = pool.Rent();
        Assert.IsNotNull(chunk);
        Assert.AreEqual(0, chunk.Count);
    }
}