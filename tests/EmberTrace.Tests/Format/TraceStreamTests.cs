using EmberTrace.Format.Internal;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Format;

[TestClass]
public class TraceStreamTests
{
    [TestMethod]
    public void OpenRead_EventsMatchRead_ForRoundTrippedSession()
    {
        var bytes = Serialize(Session(3, 50));

        var expected = TraceFormat.Read(new MemoryStream(bytes));
        using var stream = TraceFormat.OpenRead(new MemoryStream(bytes));

        CollectionAssert.AreEqual(expected.SortedEvents(), stream.Events.ToList());
        Assert.AreEqual(expected.StartTimestamp, stream.StartTimestamp);
        Assert.AreEqual(expected.EndTimestamp, stream.EndTimestamp);
        Assert.AreEqual(expected.TimestampFrequency, stream.TimestampFrequency);
        Assert.AreEqual(expected.EventCount, stream.EventCount);
        Assert.AreEqual(expected.WasOverflow, stream.WasOverflow);
        Assert.AreEqual(expected.DroppedEvents, stream.DroppedEvents);
        CollectionAssert.AreEquivalent(expected.ThreadNames.ToArray(), stream.ThreadNames.ToArray());
        Assert.IsTrue(stream.Metadata.TryGet(1, out var meta));
        Assert.AreEqual("Work", meta.Name);
    }

    [TestMethod]
    public void OpenRead_ExposesEnvironment()
    {
        var environment = new TraceEnvironment(new Dictionary<string, string> { ["os"] = "Linux", ["tag.arm"] = "B" });
        var stop = new TraceEnvironment(new Dictionary<string, string> { ["os"] = "Linux" });
        var bytes = Serialize(Session(1, 3).WithEnvironment(environment, stop));

        using var stream = TraceFormat.OpenRead(new MemoryStream(bytes));

        CollectionAssert.AreEquivalent(environment.Values.ToArray(), stream.Environment!.Values.ToArray());
        CollectionAssert.AreEquivalent(stop.Values.ToArray(), stream.EnvironmentAtStop!.Values.ToArray());
    }

    [TestMethod]
    public void OpenRead_FeedsScopeReaderWithoutBuildingASession()
    {
        var session = Session(2, 40);
        using var stream = TraceFormat.OpenRead(new MemoryStream(Serialize(session)));

        var reader = new ScopeReader(stream.Events, stream.EndTimestamp);
        var closed = reader.Read().Count(step => step.Kind == ScopeStepKind.Close);

        Assert.AreEqual(80, closed);
        Assert.AreEqual(session.EventCount, reader.TotalEventCount);
    }

    [TestMethod]
    public void OpenRead_ReadsHugeFile_WithBoundedAllocations()
    {
        var bytes = Serialize(Session(1, 500_000));

        var beforeRead = GC.GetAllocatedBytesForCurrentThread();
        var whole = TraceFormat.Read(new MemoryStream(bytes));
        var readAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeRead;
        Assert.AreEqual(1_000_000, whole.EventCount);

        var before = GC.GetAllocatedBytesForCurrentThread();
        long count = 0;
        using (var stream = TraceFormat.OpenRead(new MemoryStream(bytes)))
        {
            foreach (var _ in stream.Events)
                count++;
        }

        var streamed = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(1_000_000, count);
        Assert.IsLessThan(2_000_000, streamed);
        Assert.IsLessThan(readAllocated / 10, streamed);
    }

    [TestMethod]
    public void OpenRead_EventsEnumeratedTwice_Throws()
    {
        using var stream = TraceFormat.OpenRead(new MemoryStream(Serialize(Session(1, 3))));

        _ = stream.Events.ToList();

        Assert.ThrowsExactly<InvalidOperationException>(() => stream.Events.ToList());
    }

    [TestMethod]
    public void Dispose_BeforeEnumeration_DoesNotThrow()
    {
        var stream = TraceFormat.OpenRead(new MemoryStream(Serialize(Session(1, 3))));

        stream.Dispose();
        stream.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => stream.Events.ToList());
    }

    [TestMethod]
    public void OpenRead_Stream_LeavesTheCallersStreamOpen()
    {
        var source = new MemoryStream(Serialize(Session(1, 3)));

        using (var stream = TraceFormat.OpenRead(source))
        {
            _ = stream.Events.ToList();
        }

        Assert.IsTrue(source.CanRead);
    }

    [TestMethod]
    public void OpenRead_Path_ReleasesTheFileOnDispose()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "run.ember");
        TraceFormat.Write(Session(1, 3), path);

        using (var stream = TraceFormat.OpenRead(path))
        {
            _ = stream.Events.ToList();
        }

        File.Delete(path);
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public void OpenRead_EventsBeforeMetadata_Throws()
    {
        using var ms = new MemoryStream();
        TraceFormatWriter.WriteHeader(ms, new SessionHeader(2, false, 1_000_000, 0, 0, 0, 0, 0, 0));
        ms.WriteByte(FormatConstants.Section.Events);
        VarInt.WriteUInt64(ms, 0);
        ms.WriteByte(FormatConstants.Section.EndOfFile);
        ms.Position = 0;

        var ex = Assert.ThrowsExactly<InvalidDataException>(() => TraceFormat.OpenRead(ms));
        StringAssert.Contains(ex.Message, "Events");
        ms.Position = 0;
        Assert.AreEqual(0, TraceFormat.Read(ms).EventCount);
    }

    [TestMethod]
    public void OpenRead_TruncatedSeekableFile_FailsAtOpen()
    {
        var bytes = Serialize(Session(1, 20));

        Assert.ThrowsExactly<InvalidDataException>(() => TraceFormat.OpenRead(new MemoryStream(bytes[..^5])));
    }

    [TestMethod]
    public void OpenRead_TruncatedNonSeekableStream_ThrowsWhileEnumerating()
    {
        var bytes = Serialize(Session(1, 20));
        using var stream = TraceFormat.OpenRead(new NonSeekableStream(bytes[..^5]));

        Assert.ThrowsExactly<EndOfStreamException>(() => stream.Events.ToList());
    }

    [TestMethod]
    public void OpenRead_MissingEndOfFile_ThrowsAfterTheLastEvent()
    {
        var bytes = Serialize(Session(1, 5));
        using var stream = TraceFormat.OpenRead(new NonSeekableStream(bytes[..^1]));

        Assert.ThrowsExactly<EndOfStreamException>(() => stream.Events.ToList());
    }

    [TestMethod]
    public void OpenRead_Version1File_HasNullEnvironment()
    {
        var bytes = Convert.FromHexString(
            "454D425254524300010000005000000040420F00000000000000000000000000E803000000000000020000000000000000000000000000000000000000000000000000000000000000000000000000000100020003028164020202E2F4030200");

        using var stream = TraceFormat.OpenRead(new MemoryStream(bytes));

        Assert.IsNull(stream.Environment);
        Assert.HasCount(2, stream.Events.ToList());
    }

    [TestMethod]
    public void OpenRead_NullArguments_Throw()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => TraceFormat.OpenRead((Stream)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => TraceFormat.OpenRead((string)null!));
    }

    private static byte[] Serialize(TraceSession session)
    {
        using var ms = new MemoryStream();
        TraceFormat.Write(session, ms);
        return ms.ToArray();
    }

    private static TraceSession Session(int threads, int spansPerThread)
    {
        var script = new TraceScript();
        for (var thread = 1; thread <= threads; thread++)
            for (var i = 0; i < spansPerThread; i++)
                script.Span(1, i * 100L + thread, i * 100L + 50 + thread, thread);

        return script.ToSession(
            metadata: Meta.Of((1, "Work", "Cat")),
            threadNames: Enumerable.Range(1, threads).ToDictionary(t => t, t => "worker-" + t));
    }
}

internal static class TraceSessionTestExtensions
{
    public static TraceSession WithEnvironment(this TraceSession session, TraceEnvironment start,
        TraceEnvironment? stop)
    {
        return TraceSession.FromEvents(
            session.SortedEvents(),
            session.StartTimestamp,
            session.EndTimestamp,
            session.TimestampFrequency,
            session.ThreadNames,
            session.Metadata,
            environment: start,
            environmentAtStop: stop);
    }
}

internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("embertrace-stream").FullName;

    public void Dispose()
    {
        Directory.Delete(Path, true);
    }
}