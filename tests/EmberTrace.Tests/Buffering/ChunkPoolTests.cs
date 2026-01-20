using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EmberTrace.Internal.Buffering;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
        for (int i = 0; i < chunks.Length; i++)
            rented.Add(pool.Rent());

        Assert.HasCount(chunks.Length, rented);

        var originals = new HashSet<Chunk>(chunks);
        var reused = rented.Count(c => originals.Contains(c));
        Assert.AreEqual(chunks.Length, reused);
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
