using EmberTrace.Internal.Buffering;

namespace EmberTrace.Tests.Buffering;

[TestClass]
public class ChunkPoolTests
{
    [TestMethod]
    public void Rent_FromAnEmptyPool_CreatesAnEmptyChunkOfThePoolCapacity()
    {
        var chunk = new ChunkPool(4).Rent();

        Assert.AreEqual(0, chunk.Count);
        Assert.HasCount(4, chunk.Events);
    }

    [TestMethod]
    public void Return_ResetsTheChunk_AndRentHandsTheSameInstanceBack()
    {
        var pool = new ChunkPool(4);
        var chunk = pool.Rent();
        chunk.TryWrite(Collectors.Event());
        var version = chunk.Version;

        pool.Return(chunk);

        Assert.AreEqual(0, chunk.Count);
        Assert.IsGreaterThan(version, chunk.Version);

        chunk.Count = 3;
        var rented = pool.Rent();

        Assert.AreSame(chunk, rented);
        Assert.AreEqual(0, rented.Count);
    }

    [TestMethod]
    public void ReturnAndRent_MultiThreaded_PreservesAllChunks()
    {
        var pool = new ChunkPool(8);
        var chunks = Enumerable.Range(0, 1000).Select(_ => new Chunk(8)).ToArray();

        Parallel.For(0, chunks.Length, i => pool.Return(chunks[i]));

        var rented = new HashSet<Chunk>();
        for (var i = 0; i < chunks.Length; i++)
            rented.Add(pool.Rent());

        Assert.IsTrue(rented.SetEquals(chunks));
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

        Assert.AreEqual(0, collisions);
    }
}
