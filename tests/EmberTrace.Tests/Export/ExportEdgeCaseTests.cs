using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using EmberTrace.Internal.Buffering;
using EmberTrace.Internal.Time;
using EmberTrace.Sessions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmberTrace.Tests.Export;

[TestClass]
public class ExportEdgeCaseTests
{
    [TestMethod]
    public void WriteChromeComplete_ZeroDurationScopes_AreEmitted()
    {
        const int id = 4242;
        const int count = 5;

        var events = new List<TraceEvent>();
        for (int i = 0; i < count; i++)
        {
            events.Add(new TraceEvent(id, 1, i + 1, TraceEventKind.Begin, 0, 0));
            events.Add(new TraceEvent(id, 1, i + 1, TraceEventKind.End, 0, 0));
        }

        var session = BuildSession(events);

        using var ms = new MemoryStream();
        TraceExport.WriteChromeComplete(session, ms);
        ms.Position = 0;

        using var doc = JsonDocument.Parse(ms);
        var complete = doc.RootElement.GetProperty("traceEvents")
            .EnumerateArray()
            .Where(e => e.GetProperty("ph").GetString() == "X")
            .ToList();

        Assert.HasCount(count, complete, "sub-tick scopes must be exported, not dropped");
        foreach (var e in complete)
            Assert.AreEqual(0.0, e.GetProperty("dur").GetDouble());
    }

    [TestMethod]
    [DoNotParallelize]
    public void MarkedComplete_OversizedTag_KeepsFileNameWithinFileSystemLimit()
    {
        var cwd = Directory.GetCurrentDirectory();
        var temp = Directory.CreateTempSubdirectory("embertrace-marked");
        Directory.SetCurrentDirectory(temp.FullName);
        try
        {
            var result = TraceExport.MarkedCompleteEx(static () => { }, tag: new string('ф', 500_000));

            var fileName = Path.GetFileName(result.SlicePath);
            Assert.IsLessThanOrEqualTo(255, Encoding.UTF8.GetByteCount(fileName));
            Assert.IsTrue(File.Exists(result.SlicePath));
        }
        finally
        {
            Directory.SetCurrentDirectory(cwd);
            temp.Delete(recursive: true);
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public void MarkedComplete_SliceAndResume_ResumesWithOriginalOptionsWhenWriteFails()
    {
        var blocked = Directory.CreateTempSubdirectory("embertrace-blocked");
        var outputPath = Path.Combine(blocked.FullName, "slice.json");
        Directory.CreateDirectory(outputPath);

        Tracer.Start(new SessionOptions { ChunkCapacity = 4242 });
        try
        {
            Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
                TraceExport.MarkedCompleteEx("marked", outputPath, static () => { }, MarkedRunningSessionMode.SliceAndResume));

            Assert.IsTrue(Tracer.IsRunning, "the tracer must be resumed even when the slice cannot be written");
            Assert.AreEqual(4242, Tracer.Stop().Options.ChunkCapacity,
                "the resumed session must inherit the stopped session's options");
        }
        finally
        {
            if (Tracer.IsRunning)
                Tracer.Stop();

            blocked.Delete(recursive: true);
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public void MarkedComplete_BodyThrows_PreservesOriginalStackTrace()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        try
        {
            var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
                TraceExport.MarkedCompleteEx("marked", outputPath, static () => ThrowFromBody()));

            StringAssert.Contains(ex.StackTrace ?? string.Empty, nameof(ThrowFromBody));
        }
        finally
        {
            if (Tracer.IsRunning)
                Tracer.Stop();

            File.Delete(outputPath);
        }
    }

    private static void ThrowFromBody() => throw new InvalidOperationException("body failed");

    private static TraceSession BuildSession(List<TraceEvent> events)
    {
        var chunk = new Chunk(events.Count);
        foreach (var e in events)
            chunk.TryWrite(e);

        return new TraceSession(
            new[] { chunk },
            startTimestamp: 0,
            endTimestamp: Timestamp.Frequency,
            options: new SessionOptions(),
            threadNames: new Dictionary<int, string>(),
            droppedEvents: 0,
            droppedChunks: 0,
            sampledOutEvents: 0,
            wasOverflow: false);
    }
}
