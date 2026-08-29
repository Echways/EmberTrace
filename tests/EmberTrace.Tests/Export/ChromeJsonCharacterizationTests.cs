using System.Text.Json;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Export;

[TestClass]
public class ChromeJsonCharacterizationTests
{
    private const int WorkId = 9101;

    [TestMethod]
    public void WriteChromeComplete_PlainPath_NowEmitsArgs()
    {
        var session = BuildSingleScopeSession();

        using var ms = new MemoryStream();
        TraceExport.WriteChromeComplete(session, ms);
        ms.Position = 0;

        using var doc = JsonDocument.Parse(ms);
        var complete = FindPhase(doc, "X");

        Assert.IsTrue(complete.TryGetProperty("args", out var args));
        Assert.AreEqual(WorkId, args.GetProperty("id").GetInt32());
        Assert.AreEqual(0, args.GetProperty("depth").GetInt32());
    }

    [TestMethod]
    public void WriteChromeComplete_PlainPath_EmitsCoreFields()
    {
        var session = BuildSingleScopeSession();

        using var ms = new MemoryStream();
        TraceExport.WriteChromeComplete(session, ms);
        ms.Position = 0;

        using var doc = JsonDocument.Parse(ms);
        var complete = FindPhase(doc, "X");

        Assert.AreEqual(WorkId.ToString(), complete.GetProperty("name").GetString());
        Assert.AreEqual("X", complete.GetProperty("ph").GetString());
        Assert.AreEqual(1, complete.GetProperty("pid").GetInt32());
        Assert.IsTrue(complete.TryGetProperty("dur", out _));
    }

    private static TraceSession BuildSingleScopeSession()
    {
        var events = new[]
        {
            new TraceEventRecord(WorkId, 1, 1000, TraceEventKind.Begin, 0, 0, 1, 1),
            new TraceEventRecord(WorkId, 1, 2000, TraceEventKind.End, 0, 0, 2, 1)
        };

        return TraceSession.FromEvents(events, 0, 3000, 1_000_000);
    }

    private static JsonElement FindPhase(JsonDocument doc, string phase)
    {
        foreach (var e in doc.RootElement.GetProperty("traceEvents").EnumerateArray())
        {
            if (e.TryGetProperty("ph", out var ph) && ph.GetString() == phase)
                return e;
        }

        Assert.Fail($"No event with ph={phase}");
        return default;
    }
}
