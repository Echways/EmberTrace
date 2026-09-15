using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Export;

[TestClass]
[DoNotParallelize]
public class MarkedCompleteTests
{
    private string _directory = null!;

    private string OutputPath => Path.Combine(_directory, "slice.json");

    [TestInitialize]
    public void Setup()
    {
        _directory = Directory.CreateTempSubdirectory("embertrace-marked").FullName;
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Tracer.IsRunning)
            Tracer.Stop();

        Directory.Delete(_directory, true);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MarkedComplete_WritesTheBodyUnderASyntheticMarkerRoot(bool async)
    {
        var bodyId = Tracer.Id("MarkedCompleteTests.Body");
        var options = new MarkedCompleteOptions { OutputPath = OutputPath, Pid = 7, ProcessName = "svc" };

        var result = async
            ? await TraceExport.MarkedCompleteAsync("revision", async () =>
            {
                await Task.Yield();
                Tracer.Instant(bodyId);
            }, options)
            : TraceExport.MarkedComplete("revision", () => Tracer.Instant(bodyId), options);

        Assert.IsFalse(Tracer.IsRunning);
        Assert.AreEqual("revision", result.Name);
        Assert.AreEqual(Tracer.Id("revision"), result.MarkerId);
        Assert.AreEqual(OutputPath, result.SlicePath);
        Assert.IsTrue(result.HasWindow);
        Assert.AreEqual(bodyId, result.EnumerateSliceEvents().Single().Id);
        Assert.HasCount(3, result.EnumerateSliceEvents(false));

        var events = Parse(result.SlicePath);
        var root = events.Single(e => e.GetProperty("ph").GetString() == "X");

        Assert.AreEqual("revision", root.GetProperty("name").GetString());
        Assert.AreEqual("Marked", root.GetProperty("cat").GetString());
        Assert.AreEqual(0, root.GetProperty("tid").GetInt32());
        Assert.AreEqual(result.MarkerId, root.GetProperty("args").GetProperty("id").GetInt32());
        Assert.AreEqual(bodyId.ToString(), events.Single(e => e.GetProperty("ph").GetString() == "i")
            .GetProperty("name").GetString());
        Assert.IsTrue(events.All(e => e.GetProperty("pid").GetInt32() == 7));
        Assert.AreEqual("svc", events[0].GetProperty("args").GetProperty("name").GetString());
    }

    [TestMethod]
    public void MarkedComplete_Unique_AppendsTheCallerLineToTheName()
    {
        var result = TraceExport.MarkedComplete("revision", static () => { },
            new MarkedCompleteOptions { OutputPath = OutputPath, Unique = true });

        StringAssert.Matches(result.Name, new Regex(@"^revision_L\d+$"));
        Assert.IsTrue(File.Exists(result.SlicePath));
    }

    [TestMethod]
    public void MarkedComplete_WhileATracerRuns_ThrowsByDefaultAndLeavesTheSessionAlone()
    {
        Tracer.Start();

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            TraceExport.MarkedComplete("revision", static () => { }, new MarkedCompleteOptions { OutputPath = OutputPath }));

        Assert.IsTrue(Tracer.IsRunning);
        Assert.IsFalse(File.Exists(OutputPath));
    }

    [TestMethod]
    public void MarkedComplete_SliceAndResume_SlicesTheRunningSessionAndResumesWithItsOptions()
    {
        var before = Tracer.Id("MarkedCompleteTests.Before");
        Tracer.Start(new SessionOptions { ChunkCapacity = 4242 });
        Tracer.Instant(before);

        var result = TraceExport.MarkedComplete("revision", static () => { },
            new MarkedCompleteOptions { OutputPath = OutputPath, Running = MarkedRunningSessionMode.SliceAndResume });

        Assert.IsTrue(Tracer.IsRunning);
        Assert.AreEqual(4242, Tracer.Stop().Options.ChunkCapacity);
        Assert.IsTrue(result.CapturedSession.Events().Any(e => e.Id == before));
        Assert.IsFalse(result.EnumerateSliceEvents().Any());
    }

    [TestMethod]
    public void MarkedComplete_SliceAndResume_ResumesEvenWhenTheSliceCannotBeWritten()
    {
        Directory.CreateDirectory(OutputPath);
        Tracer.Start(new SessionOptions { ChunkCapacity = 4242 });

        Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
            TraceExport.MarkedComplete("revision", static () => { },
                new MarkedCompleteOptions { OutputPath = OutputPath, Running = MarkedRunningSessionMode.SliceAndResume }));

        Assert.IsTrue(Tracer.IsRunning);
        Assert.AreEqual(4242, Tracer.Stop().Options.ChunkCapacity);
    }

    [TestMethod]
    public void MarkedComplete_BodyThrows_RethrowsWithTheOriginalStackTraceAndStillWritesTheSlice()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            TraceExport.MarkedComplete("revision", ThrowFromBody, new MarkedCompleteOptions { OutputPath = OutputPath }));

        StringAssert.Contains(ex.StackTrace, nameof(ThrowFromBody));
        Assert.IsFalse(Tracer.IsRunning);
        Assert.IsTrue(File.Exists(OutputPath));
    }

    [TestMethod]
    public void MarkedComplete_WithAnOversizedName_KeepsTheDefaultFileNameWithinTheFileSystemLimit()
    {
        var cwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_directory);
        try
        {
            var result = TraceExport.MarkedComplete(new string('ф', 500_000), static () => { }, default);

            Assert.IsLessThanOrEqualTo(255, Encoding.UTF8.GetByteCount(Path.GetFileName(result.SlicePath)));
            Assert.IsTrue(File.Exists(result.SlicePath));
        }
        finally
        {
            Directory.SetCurrentDirectory(cwd);
        }
    }

    private static void ThrowFromBody()
    {
        throw new InvalidOperationException("body failed");
    }

    private static JsonElement[] Parse(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement.GetProperty("traceEvents").EnumerateArray().Select(e => e.Clone()).ToArray();
    }
}
