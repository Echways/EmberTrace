using System.Collections.Generic;
using EmberTrace.Internal.Buffering;
using EmberTrace.Sessions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmberTrace.Tests.Buffering;

[TestClass]
public class OverflowPolicyTests
{
    [TestMethod]
    public void DropNew_ExcessEvents_AreRejectedAndCountedAsDropped()
    {
        const int limit = 5;
        var (collector, _) = MakeCollector(OverflowPolicy.DropNew, maxEvents: limit, capacity: 16);

        int accepted = 0, rejected = 0;
        for (int i = 0; i < limit * 2; i++)
        {
            if (collector.TryAcceptEvent()) accepted++;
            else rejected++;
        }

        Assert.AreEqual(limit, accepted, "Only exactly MaxTotalEvents events should be accepted");
        Assert.AreEqual(limit, rejected, "Excess events must be counted");
        Assert.AreEqual(limit, collector.DroppedEvents);
        Assert.IsFalse(collector.IsClosed, "DropNew must not close the session");
    }

    [TestMethod]
    public void DropNew_WasOverflow_SetAfterFirstExcess()
    {
        var (collector, _) = MakeCollector(OverflowPolicy.DropNew, maxEvents: 2, capacity: 16);

        collector.TryAcceptEvent();
        collector.TryAcceptEvent();
        Assert.IsFalse(collector.WasOverflow, "No overflow yet");

        collector.TryAcceptEvent();
        Assert.IsTrue(collector.WasOverflow);
    }

    [TestMethod]
    public void DropNew_OnOverflow_FiredExactlyOnceWithCorrectInfo()
    {
        var received = new List<OverflowInfo>();

        var options = new SessionOptions
        {
            MaxTotalEvents = 3,
            OverflowPolicy = OverflowPolicy.DropNew,
            ChunkCapacity = 16,
            OnOverflow = info => received.Add(info)
        };
        var pool = new ChunkPool(16);
        var collector = new SessionCollector(options, pool, 16);

        for (int i = 0; i < 10; i++)
            collector.TryAcceptEvent();

        Assert.AreEqual(1, received.Count, "OnOverflow must fire exactly once regardless of how many events overflow");
        Assert.AreEqual(OverflowReason.MaxTotalEvents, received[0].Reason);
        Assert.AreEqual(OverflowPolicy.DropNew, received[0].Policy);
    }

    [TestMethod]
    public void StopSession_MaxTotalEvents_ClosesCollectorOnFirstExcess()
    {
        var (collector, _) = MakeCollector(OverflowPolicy.StopSession, maxEvents: 3, capacity: 16);

        collector.TryAcceptEvent();
        collector.TryAcceptEvent();
        collector.TryAcceptEvent();
        Assert.IsFalse(collector.IsClosed);

        collector.TryAcceptEvent();
        Assert.IsTrue(collector.IsClosed);
        Assert.IsTrue(collector.WasOverflow);
    }

    [TestMethod]
    public void StopSession_MaxTotalEvents_AllSubsequentEventsRejected()
    {
        var (collector, _) = MakeCollector(OverflowPolicy.StopSession, maxEvents: 2, capacity: 16);

        for (int i = 0; i < 20; i++)
            collector.TryAcceptEvent();

        Assert.IsFalse(collector.TryAcceptEvent());
        Assert.IsFalse(collector.TryRentChunk(out _));
    }

    [TestMethod]
    public void StopSession_MaxTotalChunks_ClosesCollectorWhenChunkLimitExceeded()
    {
        const int maxChunks = 2;
        var options = new SessionOptions
        {
            MaxTotalChunks = maxChunks,
            OverflowPolicy = OverflowPolicy.StopSession,
            ChunkCapacity = 8
        };
        var pool = new ChunkPool(8);
        var collector = new SessionCollector(options, pool, 8);

        Assert.IsTrue(collector.TryRentChunk(out _));
        Assert.IsTrue(collector.TryRentChunk(out _));
        Assert.IsFalse(collector.IsClosed, "Not closed yet — still at limit");

        collector.TryRentChunk(out _);
        Assert.IsTrue(collector.IsClosed);
        Assert.IsTrue(collector.WasOverflow);
    }

    [TestMethod]
    public void DropOldest_MaxTotalEvents_DropsOldestChunkAndAcceptsNewEvent()
    {
        const int capacity = 4;
        var (collector, _) = MakeCollector(OverflowPolicy.DropOldest, maxEvents: capacity, capacity: capacity);

        Assert.IsTrue(collector.TryRentChunk(out var chunk));
        var dummyEvent = new TraceEvent(1, 1, 100L, TraceEventKind.Begin, 0, 0);
        for (int i = 0; i < capacity; i++)
        {
            Assert.IsTrue(collector.TryAcceptEvent());
            chunk!.TryWrite(dummyEvent);
        }

        collector.MarkChunkInactive(chunk!);

        bool accepted = collector.TryAcceptEvent();

        Assert.IsTrue(accepted, "DropOldest should accept the new event after evicting oldest chunk");
        Assert.IsFalse(collector.IsClosed, "Session must remain open");
        Assert.IsTrue(collector.WasOverflow);
        Assert.AreEqual(1L, collector.DroppedChunks, "Exactly one chunk should be dropped");
        Assert.AreEqual((long)capacity, collector.DroppedEvents, "All events in dropped chunk counted as dropped");
    }

    [TestMethod]
    public void DropOldest_MaxTotalEvents_WhenNoInactiveChunkAvailable_FallsBackToDropNew()
    {
        const int capacity = 4;
        var (collector, _) = MakeCollector(OverflowPolicy.DropOldest, maxEvents: capacity, capacity: capacity);

        for (int i = 0; i < capacity; i++)
            Assert.IsTrue(collector.TryAcceptEvent());

        Assert.IsTrue(collector.TryRentChunk(out _));

        bool accepted = collector.TryAcceptEvent();

        Assert.IsFalse(accepted, "No inactive chunk to drop → event is rejected");
        Assert.IsTrue(collector.WasOverflow);
        Assert.IsFalse(collector.IsClosed);
    }

    [TestMethod]
    public void DropOldest_MaxTotalChunks_ReusesDroppedChunk()
    {
        const int maxChunks = 2;
        const int capacity = 8;
        var options = new SessionOptions
        {
            MaxTotalChunks = maxChunks,
            OverflowPolicy = OverflowPolicy.DropOldest,
            ChunkCapacity = capacity
        };
        var pool = new ChunkPool(capacity);
        var collector = new SessionCollector(options, pool, capacity);

        Assert.IsTrue(collector.TryRentChunk(out var chunk1));
        collector.MarkChunkInactive(chunk1!);

        Assert.IsTrue(collector.TryRentChunk(out var chunk2));
        collector.MarkChunkInactive(chunk2!);

        bool rented = collector.TryRentChunk(out var chunk3);

        Assert.IsTrue(rented, "DropOldest should succeed by evicting the oldest inactive chunk");
        Assert.IsNotNull(chunk3);
        Assert.AreEqual(1L, collector.DroppedChunks);
        Assert.IsTrue(collector.WasOverflow);
        Assert.IsFalse(collector.IsClosed);
    }

    [TestMethod]
    public void DropOldest_MaxTotalChunks_WhenNoInactiveChunk_ReturnsFalse()
    {
        const int maxChunks = 1;
        var options = new SessionOptions
        {
            MaxTotalChunks = maxChunks,
            OverflowPolicy = OverflowPolicy.DropOldest,
            ChunkCapacity = 8
        };
        var pool = new ChunkPool(8);
        var collector = new SessionCollector(options, pool, 8);

        Assert.IsTrue(collector.TryRentChunk(out _));

        bool rented = collector.TryRentChunk(out _);
        Assert.IsFalse(rented);
        Assert.IsTrue(collector.WasOverflow);
        Assert.IsFalse(collector.IsClosed);
    }

    [DataTestMethod]
    [DataRow(OverflowPolicy.DropNew)]
    [DataRow(OverflowPolicy.DropOldest)]
    public void HandleRateLimitExceeded_NonStopPolicies_KeepSessionOpen(OverflowPolicy policy)
    {
        var (collector, _) = MakeCollector(policy, maxEvents: 0, capacity: 16);

        collector.HandleRateLimitExceeded();

        Assert.IsFalse(collector.IsClosed);
        Assert.AreEqual(1L, collector.DroppedEvents);
        Assert.IsTrue(collector.WasOverflow);
    }

    [TestMethod]
    public void HandleRateLimitExceeded_StopSession_ClosesCollector()
    {
        var (collector, _) = MakeCollector(OverflowPolicy.StopSession, maxEvents: 0, capacity: 16);

        collector.HandleRateLimitExceeded();

        Assert.IsTrue(collector.IsClosed);
    }

    [TestMethod]
    public void RecordSampledOutEvent_AccumulatesCount()
    {
        var (collector, _) = MakeCollector(OverflowPolicy.DropNew, maxEvents: 0, capacity: 16);

        collector.RecordSampledOutEvent();
        collector.RecordSampledOutEvent();
        collector.RecordSampledOutEvent();

        Assert.AreEqual(3L, collector.SampledOutEvents);
    }

    private static (SessionCollector collector, ChunkPool pool) MakeCollector(
        OverflowPolicy policy, long maxEvents, int capacity)
    {
        var options = new SessionOptions
        {
            MaxTotalEvents = maxEvents,
            OverflowPolicy = policy,
            ChunkCapacity = capacity
        };
        var pool = new ChunkPool(capacity);
        return (new SessionCollector(options, pool, capacity), pool);
    }
}
