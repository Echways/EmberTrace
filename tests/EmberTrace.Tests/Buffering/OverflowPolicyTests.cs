using System.Collections.Concurrent;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Buffering;

[TestClass]
public class OverflowPolicyTests
{
    [TestMethod]
    [DataRow(OverflowPolicy.DropNew, false, 2L)]
    [DataRow(OverflowPolicy.DropOldest, false, 2L)]
    [DataRow(OverflowPolicy.StopSession, true, 1L)]
    public void MaxTotalEvents_AcceptsUpToTheLimitThenAppliesThePolicy(
        OverflowPolicy policy, bool closes, long expectedDropped)
    {
        var collector = Collectors.Create(policy, maxEvents: 3);

        for (var i = 0; i < 3; i++)
            Assert.IsTrue(collector.TryAcceptEvent());

        Assert.IsFalse(collector.WasOverflow);

        Assert.IsFalse(collector.TryAcceptEvent());
        Assert.IsFalse(collector.TryAcceptEvent());

        Assert.IsTrue(collector.WasOverflow);
        Assert.AreEqual(closes, collector.IsClosed);
        Assert.AreEqual(expectedDropped, collector.DroppedEvents);
        Assert.AreEqual(!closes, collector.TryRentChunk(out _));
    }

    [TestMethod]
    [DataRow(OverflowPolicy.DropNew)]
    [DataRow(OverflowPolicy.DropOldest)]
    [DataRow(OverflowPolicy.StopSession)]
    public void OnOverflow_FiresOnceOffTheWritingThreadWithTheReasonAndPolicy(OverflowPolicy policy)
    {
        var received = new ConcurrentQueue<(OverflowInfo Info, int Thread)>();
        using var fired = new ManualResetEventSlim();

        var collector = Collectors.Create(policy, maxEvents: 3, onOverflow: info =>
        {
            received.Enqueue((info, Environment.CurrentManagedThreadId));
            fired.Set();
        });

        for (var i = 0; i < 10; i++)
            collector.TryAcceptEvent();

        Assert.IsTrue(fired.Wait(TimeSpan.FromSeconds(10)));
        Assert.HasCount(1, received);
        Assert.IsTrue(received.TryDequeue(out var notification));
        Assert.AreEqual(OverflowReason.MaxTotalEvents, notification.Info.Reason);
        Assert.AreEqual(policy, notification.Info.Policy);
        Assert.AreNotEqual(Environment.CurrentManagedThreadId, notification.Thread);
    }

    [TestMethod]
    public void OnOverflow_HandlerThatThrows_DoesNotBreakTheCollector()
    {
        using var fired = new ManualResetEventSlim();
        var collector = Collectors.Create(maxEvents: 1, onOverflow: _ =>
        {
            fired.Set();
            throw new InvalidOperationException();
        });

        collector.TryAcceptEvent();
        collector.TryAcceptEvent();

        Assert.IsTrue(fired.Wait(TimeSpan.FromSeconds(10)));
        Assert.IsFalse(collector.IsClosed);
    }

    [TestMethod]
    public void StopSession_MaxTotalChunks_ClosesWhenTheLimitIsExceeded()
    {
        var collector = Collectors.Create(OverflowPolicy.StopSession, maxChunks: 2, capacity: 8);

        Assert.IsTrue(collector.TryRentChunk(out _));
        Assert.IsTrue(collector.TryRentChunk(out _));
        Assert.IsFalse(collector.IsClosed);

        Assert.IsFalse(collector.TryRentChunk(out _));
        Assert.IsTrue(collector.IsClosed);
        Assert.IsTrue(collector.WasOverflow);
    }

    [TestMethod]
    public void DropNew_MaxTotalChunks_RefusesWithoutClosing()
    {
        var collector = Collectors.Create(OverflowPolicy.DropNew, maxChunks: 1, capacity: 8);

        Assert.IsTrue(collector.TryRentChunk(out _));
        Assert.IsFalse(collector.TryRentChunk(out _));

        Assert.IsFalse(collector.IsClosed);
        Assert.HasCount(1, collector.Chunks);
    }

    [TestMethod]
    public void DropOldest_MaxTotalEvents_EvictsTheOldestInactiveChunk()
    {
        const int capacity = 4;
        var collector = Collectors.Create(OverflowPolicy.DropOldest, maxEvents: capacity, capacity: capacity);

        Assert.IsTrue(collector.TryRentChunk(out var chunk));
        for (var i = 0; i < capacity; i++)
        {
            Assert.IsTrue(collector.TryAcceptEvent());
            chunk!.TryWrite(Collectors.Event());
        }

        collector.MarkChunkInactive(chunk!);

        Assert.IsTrue(collector.TryAcceptEvent());
        Assert.IsFalse(collector.IsClosed);
        Assert.IsTrue(collector.WasOverflow);
        Assert.AreEqual(1L, collector.DroppedChunks);
        Assert.AreEqual(capacity, collector.DroppedEvents);
        Assert.IsEmpty(collector.Chunks);
    }

    [TestMethod]
    public void DropOldest_MaxTotalEvents_WithoutAnInactiveChunk_RejectsTheEvent()
    {
        const int capacity = 4;
        var collector = Collectors.Create(OverflowPolicy.DropOldest, maxEvents: capacity, capacity: capacity);

        for (var i = 0; i < capacity; i++)
            Assert.IsTrue(collector.TryAcceptEvent());

        Assert.IsTrue(collector.TryRentChunk(out _));

        Assert.IsFalse(collector.TryAcceptEvent());
        Assert.IsTrue(collector.WasOverflow);
        Assert.IsFalse(collector.IsClosed);
        Assert.AreEqual(0L, collector.DroppedChunks);
    }

    [TestMethod]
    public void DropOldest_MaxTotalChunks_RecyclesTheOldestInactiveChunk()
    {
        var collector = Collectors.Create(OverflowPolicy.DropOldest, maxChunks: 2, capacity: 8);

        Assert.IsTrue(collector.TryRentChunk(out var first));
        collector.MarkChunkInactive(first!);
        Assert.IsTrue(collector.TryRentChunk(out var second));
        collector.MarkChunkInactive(second!);

        Assert.IsTrue(collector.TryRentChunk(out var third));

        Assert.AreSame(first, third);
        CollectionAssert.AreEqual(new[] { second, third }, collector.Chunks.ToArray());
        Assert.AreEqual(1L, collector.DroppedChunks);
        Assert.IsTrue(collector.WasOverflow);
        Assert.IsFalse(collector.IsClosed);
    }

    [TestMethod]
    public void DropOldest_MaxTotalChunks_WithoutAnInactiveChunk_RefusesWithoutClosing()
    {
        var collector = Collectors.Create(OverflowPolicy.DropOldest, maxChunks: 1, capacity: 8);

        Assert.IsTrue(collector.TryRentChunk(out _));

        Assert.IsFalse(collector.TryRentChunk(out _));
        Assert.IsTrue(collector.WasOverflow);
        Assert.IsFalse(collector.IsClosed);
    }

    [TestMethod]
    public void DropOldest_EventLimitWithoutAChunkLimit_DerivesTheChunkLimit()
    {
        var collector = Collectors.Create(OverflowPolicy.DropOldest, maxEvents: 5, capacity: 4);

        Assert.IsTrue(collector.TryRentChunk(out _));
        Assert.IsTrue(collector.TryRentChunk(out _));
        Assert.IsFalse(collector.TryRentChunk(out _));
    }

    [TestMethod]
    public void DropOldest_EvictedChunkEvents_AreCountedWithoutEnablingAnEventLimit()
    {
        const int capacity = 4;
        var collector = Collectors.Create(OverflowPolicy.DropOldest, maxChunks: 1, capacity: capacity);

        Assert.IsTrue(collector.TryRentChunk(out var chunk));
        for (var i = 0; i < capacity; i++)
        {
            Assert.IsTrue(collector.TryAcceptEvent());
            chunk!.TryWrite(Collectors.Event());
        }

        collector.MarkChunkInactive(chunk!);
        Assert.IsTrue(collector.TryRentChunk(out _));

        Assert.AreEqual(1L, collector.DroppedChunks);
        Assert.AreEqual(capacity, collector.DroppedEvents);

        for (var i = 0; i < capacity * 4; i++)
            Assert.IsTrue(collector.TryAcceptEvent());
    }

    [TestMethod]
    [DataRow(OverflowPolicy.DropNew, false)]
    [DataRow(OverflowPolicy.DropOldest, false)]
    [DataRow(OverflowPolicy.StopSession, true)]
    public void HandleRateLimitExceeded_CountsTheDropAndClosesOnlyForStopSession(OverflowPolicy policy, bool closes)
    {
        var collector = Collectors.Create(policy);

        Assert.IsFalse(collector.HandleRateLimitExceeded());

        Assert.AreEqual(closes, collector.IsClosed);
        Assert.AreEqual(1L, collector.DroppedEvents);
        Assert.IsTrue(collector.WasOverflow);
    }

    [TestMethod]
    public void OverflowPolicy_NamesAreStableAndDropIsAliasOfDropNew()
    {
        Assert.AreEqual("DropNew", OverflowPolicy.DropNew.ToString());
        Assert.AreEqual("DropOldest", OverflowPolicy.DropOldest.ToString());
        Assert.AreEqual("StopSession", OverflowPolicy.StopSession.ToString());
        Assert.AreEqual(OverflowPolicy.DropNew, Enum.Parse<OverflowPolicy>("Drop"));
        Assert.HasCount(3, new HashSet<OverflowPolicy>(Enum.GetValues<OverflowPolicy>()));
    }
}
