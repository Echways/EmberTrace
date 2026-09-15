using EmberTrace.Format.Internal;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Format;

[TestClass]
public class TraceFormatRoundTripTests
{
    [TestMethod]
    public void Header_RoundTripsEveryField()
    {
        var header = new SessionHeader(
            FormatConstants.Version, true, 10_000_000, 111, 222, 3, 4, 5, 6, true, 638_000_000_000_000_000);

        using var ms = new MemoryStream();
        TraceFormatWriter.WriteHeader(ms, header);

        Assert.AreEqual(FormatConstants.HeaderSize, ms.Length);

        ms.Position = 0;
        Assert.AreEqual(header, TraceFormatReader.ReadHeader(ms));
    }

    [TestMethod]
    public void ThreadNames_RoundTrip()
    {
        var names = new Dictionary<int, string> { [1] = "main", [7] = "поток-7", [-3] = "" };

        using var ms = new MemoryStream();
        TraceFormatWriter.WriteThreadNames(ms, names);
        ms.Position = 0;

        Assert.AreEqual(FormatConstants.Section.ThreadNames, ms.ReadByte());
        CollectionAssert.AreEquivalent(names, TraceFormatReader.ReadThreadNames(ms).ToArray());
    }

    [TestMethod]
    public void Metadata_RoundTripsIncludingNullCategory()
    {
        var entries = new List<TraceMeta>
        {
            new(1000, "App", "App"),
            new(2000, "Worker", null),
            new(-1, "GC.Gen0", "Runtime")
        };

        using var ms = new MemoryStream();
        TraceFormatWriter.WriteMetadata(ms, entries);
        ms.Position = 0;

        Assert.AreEqual(FormatConstants.Section.Metadata, ms.ReadByte());
        CollectionAssert.AreEqual(entries, TraceFormatReader.ReadMetadata(ms));
    }

    [TestMethod]
    public void Events_RoundTripAllFields()
    {
        var events = new List<TraceEventRecord>
        {
            new(1000, 7, 100, TraceEventKind.Begin, 0, 0, 1, 3),
            new(2000, 7, 250, TraceEventKind.Begin, 55, 66, 2, 3),
            new(2000, 9, 400, TraceEventKind.End, 55, 66, 1, 4),
            new(3000, 7, 900, TraceEventKind.Counter, 0, -12345, 3, 3),
            new(4000, 7, 900, TraceEventKind.Instant, 0, 0, 7, 3),
            new(5000, 9, 950, TraceEventKind.FlowStep, long.MaxValue, 0, 2, 4),
            new(1000, 7, 960, TraceEventKind.End, 0, 0, 8, 3)
        };

        using var ms = new MemoryStream();
        TraceFormatWriter.WriteEvents(ms, events, events.Count);
        ms.Position = 0;

        Assert.AreEqual(FormatConstants.Section.Events, ms.ReadByte());
        CollectionAssert.AreEqual(events, TraceFormatReader.ReadEvents(ms));
    }

    [TestMethod]
    public void Events_EmptySequence_RoundTrips()
    {
        using var ms = new MemoryStream();
        TraceFormatWriter.WriteEvents(ms, [], 0);
        ms.Position = 0;

        Assert.AreEqual(FormatConstants.Section.Events, ms.ReadByte());
        Assert.IsEmpty(TraceFormatReader.ReadEvents(ms));
    }

    [TestMethod]
    public void Events_TypicalScopePairsStayUnderEightBytesEach()
    {
        var events = Enumerable.Range(0, 1000)
            .Select(i => new TraceEventRecord(1000, 7, 100 + i, TraceEventKind.Begin, 0, 0, i + 1, 3))
            .ToList();

        using var ms = new MemoryStream();
        TraceFormatWriter.WriteEvents(ms, events, events.Count);

        Assert.IsLessThan(8.0, (double)(ms.Length - 1) / events.Count);
    }

    [TestMethod]
    public void WriteEvents_OnDecreasingTimestamps_Throws()
    {
        var events = new List<TraceEventRecord>
        {
            new(1, 1, 200, TraceEventKind.Begin, 0, 0, 1),
            new(1, 1, 100, TraceEventKind.End, 0, 0, 2)
        };

        using var ms = new MemoryStream();

        Assert.ThrowsExactly<InvalidOperationException>(() => TraceFormatWriter.WriteEvents(ms, events, events.Count));
    }

    [TestMethod]
    public void Session_FullRoundTrip_PreservesEventsMetadataAndCounters()
    {
        var events = new List<TraceEventRecord>
        {
            new(1000, 7, 100, TraceEventKind.Begin, 0, 0, 1, 3),
            new(2100, 7, 150, TraceEventKind.Begin, 0, 0, 2, 3),
            new(2100, 7, 300, TraceEventKind.End, 0, 0, 3, 3),
            new(3000, 9, 350, TraceEventKind.FlowStart, 42, 0, 1, 4),
            new(3000, 9, 500, TraceEventKind.FlowEnd, 42, 0, 2, 4),
            new(1000, 7, 900, TraceEventKind.End, 0, 0, 4, 3)
        };

        var original = TraceSession.FromEvents(
            events, 100, 900, 1_000_000,
            new Dictionary<int, string> { [7] = "main", [9] = "worker" },
            Meta.Of((1000, "App", "App"), (2100, "CpuWork", "CPU"), (9999, "Unused", null)),
            droppedEvents: 2, droppedChunks: 1, sampledOutEvents: 3, wasOverflow: true);

        var loaded = RoundTrip(original);

        Assert.AreEqual(original.StartTimestamp, loaded.StartTimestamp);
        Assert.AreEqual(original.EndTimestamp, loaded.EndTimestamp);
        Assert.AreEqual(original.TimestampFrequency, loaded.TimestampFrequency);
        Assert.AreEqual(original.DroppedEvents, loaded.DroppedEvents);
        Assert.AreEqual(original.DroppedChunks, loaded.DroppedChunks);
        Assert.AreEqual(original.SampledOutEvents, loaded.SampledOutEvents);
        Assert.AreEqual(original.WasOverflow, loaded.WasOverflow);
        CollectionAssert.AreEquivalent(original.ThreadNames.ToArray(), loaded.ThreadNames.ToArray());
        CollectionAssert.AreEqual(original.SortedEvents(), loaded.SortedEvents());

        Assert.IsTrue(loaded.Metadata.TryGet(2100, out var cpu));
        Assert.AreEqual(new TraceMeta(2100, "CpuWork", "CPU"), cpu);
        Assert.IsFalse(loaded.Metadata.TryGet(3000, out _));
        Assert.IsFalse(loaded.Metadata.TryGet(9999, out _));
    }

    [TestMethod]
    public void Session_RoundTrip_ProducesIdenticalAnalysis()
    {
        var script = new TraceScript();
        for (var i = 0; i < 500; i++)
            script.Span(1000, i * 1500L, i * 1500L + 1000);

        var original = script.ToSession();
        var loaded = RoundTrip(original);

        var before = original.Analyze().ByTotalTimeDesc.Single();
        var after = loaded.Analyze().ByTotalTimeDesc.Single();

        Assert.AreEqual(original.DurationMs, loaded.DurationMs, 1e-9);
        Assert.AreEqual(before.Count, after.Count);
        Assert.AreEqual(before.TotalMs, after.TotalMs, 1e-9);
        Assert.AreEqual(before.P95Ms, after.P95Ms, 1e-9);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void RecordedSession_RoundTripKeepsTheSnapshotFlagAndTheStartAnchor(bool snapshot)
    {
        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions { ChunkCapacity = 1024 });
        tracing.Instant(11);
        tracing.Instant(12);

        var session = snapshot ? tracing.Snapshot() : tracing.Stop();
        var loaded = RoundTrip(session);

        Assert.AreEqual(snapshot, loaded.IsSnapshot);
        Assert.AreEqual(session.StartedAtUtc, loaded.StartedAtUtc);
        CollectionAssert.AreEqual(session.SortedEvents(), loaded.SortedEvents());
    }

    [TestMethod]
    public void WriteToAPath_CreatesMissingDirectoriesAndReadsBack()
    {
        var root = Path.Combine(Path.GetTempPath(), $"embertrace-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "nested", "trace" + TraceFormat.FileExtension);

        try
        {
            var original = new TraceScript().Instant(1, 10).ToSession(start: 10);

            TraceFormat.Write(original, path);

            CollectionAssert.AreEqual(original.SortedEvents(), TraceFormat.Read(path).SortedEvents());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void NullArguments_Throw()
    {
        var session = new TraceScript().ToSession();

        Assert.ThrowsExactly<ArgumentNullException>(() => TraceFormat.Write(null!, Stream.Null));
        Assert.ThrowsExactly<ArgumentNullException>(() => TraceFormat.Write(session, (Stream)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => TraceFormat.Write(session, (string)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => TraceFormat.Read((Stream)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => TraceFormat.Read((string)null!));
    }

    private static TraceSession RoundTrip(TraceSession session)
    {
        using var ms = new MemoryStream();
        TraceFormat.Write(session, ms);
        ms.Position = 0;
        return TraceFormat.Read(ms);
    }
}
