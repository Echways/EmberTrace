using EmberTrace.Format.Internal;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Format;

[TestClass]
public class TraceFormatRoundTripTests
{
    [TestMethod]
    public void ThreadNames_RoundTrip()
    {
        var names = new Dictionary<int, string> { [1] = "main", [7] = "поток-7", [-3] = "" };

        using var ms = new MemoryStream();
        TraceFormatWriter.WriteThreadNames(ms, names);
        ms.Position = 0;

        Assert.AreEqual(FormatConstants.Section.ThreadNames, ms.ReadByte());
        var read = TraceFormatReader.ReadThreadNames(ms);

        Assert.HasCount(3, read);
        Assert.AreEqual("main", read[1]);
        Assert.AreEqual("поток-7", read[7]);
        Assert.AreEqual("", read[-3]);
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
        var read = TraceFormatReader.ReadMetadata(ms);

        CollectionAssert.AreEqual(entries, read);
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
            new(1000, 7, 901, TraceEventKind.End, 0, 0, 4, 3)
        };

        using var ms = new MemoryStream();
        TraceFormatWriter.WriteEvents(ms, events, events.Count);
        ms.Position = 0;

        Assert.AreEqual(FormatConstants.Section.Events, ms.ReadByte());
        var read = TraceFormatReader.ReadEvents(ms);

        CollectionAssert.AreEqual(events, read);
    }

    [TestMethod]
    public void Events_TypicalScopePairsStayUnderEightBytesEach()
    {
        var events = new List<TraceEventRecord>();
        for (var i = 0; i < 1000; i++)
            events.Add(new TraceEventRecord(1000, 7, 100 + i, TraceEventKind.Begin, 0, 0, i + 1, 3));

        using var ms = new MemoryStream();
        TraceFormatWriter.WriteEvents(ms, events, events.Count);

        var bytesPerEvent = (double)(ms.Length - 1) / events.Count;
        Assert.IsLessThan(8.0, bytesPerEvent, $"expected dense encoding, got {bytesPerEvent:F2} bytes/event");
    }

    [TestMethod]
    public void Events_EmptySequence_RoundTrips()
    {
        using var ms = new MemoryStream();
        TraceFormatWriter.WriteEvents(ms, Array.Empty<TraceEventRecord>(), 0);
        ms.Position = 0;

        Assert.AreEqual(FormatConstants.Section.Events, ms.ReadByte());
        Assert.IsEmpty(TraceFormatReader.ReadEvents(ms));
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

        Assert.ThrowsExactly<InvalidOperationException>(
            () => TraceFormatWriter.WriteEvents(ms, events, events.Count));
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

        var metadata = TraceMetadata.FromEntries(new[]
        {
            new TraceMeta(1000, "App", "App"),
            new TraceMeta(2100, "CpuWork", "CPU"),
            new TraceMeta(3000, "JobFlow", "Flow")
        });

        var original = TraceSession.FromEvents(
            events, 100, 900, 1_000_000,
            new Dictionary<int, string> { [7] = "main", [9] = "worker" },
            metadata,
            droppedEvents: 2, droppedChunks: 1, sampledOutEvents: 3, wasOverflow: true);

        using var ms = new MemoryStream();
        TraceFormat.Write(original, ms);
        ms.Position = 0;
        var loaded = TraceFormat.Read(ms);

        Assert.AreEqual(original.StartTimestamp, loaded.StartTimestamp);
        Assert.AreEqual(original.EndTimestamp, loaded.EndTimestamp);
        Assert.AreEqual(original.TimestampFrequency, loaded.TimestampFrequency);
        Assert.AreEqual(original.EventCount, loaded.EventCount);
        Assert.AreEqual(original.DroppedEvents, loaded.DroppedEvents);
        Assert.AreEqual(original.DroppedChunks, loaded.DroppedChunks);
        Assert.AreEqual(original.SampledOutEvents, loaded.SampledOutEvents);
        Assert.AreEqual(original.WasOverflow, loaded.WasOverflow);
        Assert.AreEqual("main", loaded.ThreadNames[7]);
        Assert.AreEqual("worker", loaded.ThreadNames[9]);

        Assert.IsTrue(loaded.Metadata.TryGet(2100, out var cpu));
        Assert.AreEqual("CpuWork", cpu.Name);
        Assert.AreEqual("CPU", cpu.Category);

        CollectionAssert.AreEqual(Sorted(original), Sorted(loaded));
    }

    [TestMethod]
    public void Session_RoundTrip_ProducesIdenticalAnalysis()
    {
        var events = new List<TraceEventRecord>();
        long timestamp = 0;
        for (var i = 0; i < 500; i++)
        {
            events.Add(new TraceEventRecord(1000, 7, timestamp, TraceEventKind.Begin, 0, 0, events.Count + 1, 3));
            timestamp += 1000;
            events.Add(new TraceEventRecord(1000, 7, timestamp, TraceEventKind.End, 0, 0, events.Count + 1, 3));
            timestamp += 500;
        }

        var original = TraceSession.FromEvents(events, 0, timestamp, 1_000_000);

        using var ms = new MemoryStream();
        TraceFormat.Write(original, ms);
        ms.Position = 0;
        var loaded = TraceFormat.Read(ms);

        var before = original.Analyze();
        var after = loaded.Analyze();

        Assert.AreEqual(before.DurationMs, after.DurationMs, 0.0001);
        Assert.AreEqual(before.ByTotalTimeDesc[0].Count, after.ByTotalTimeDesc[0].Count);
        Assert.AreEqual(before.ByTotalTimeDesc[0].TotalMs, after.ByTotalTimeDesc[0].TotalMs, 0.0001);
    }

    [TestMethod]
    public void Write_ThenRead_ViaFilePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"embertrace-{Guid.NewGuid():N}{TraceFormat.FileExtension}");

        try
        {
            var original = TraceSession.FromEvents(
                new[] { new TraceEventRecord(1, 1, 10, TraceEventKind.Instant, 0, 0, 1) },
                10, 10, 1_000_000);

            TraceFormat.Write(original, path);
            var loaded = TraceFormat.Read(path);

            Assert.AreEqual(1, loaded.EventCount);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static TraceEventRecord[] Sorted(TraceSession session)
    {
        var list = new List<TraceEventRecord>();
        foreach (var e in session.EnumerateEventsSorted())
            list.Add(e);
        return list.ToArray();
    }
}
