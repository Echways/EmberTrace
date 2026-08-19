using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EmberTrace.Sessions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmberTrace.Tests.Export;

[TestClass]
public class ChromeAsyncExportTests
{
    [TestMethod]
    public async Task WriteChromeComplete_AsyncScope_EmitsPairedAsyncPhases()
    {
        const int outer = 8001;
        const int inner = 8002;

        var ts = new TracingSession();
        ts.Start(new SessionOptions { ChunkCapacity = 256 });

        TraceSession session;
        try
        {
            await using (ts.ScopeAsync(outer))
            {
                await Task.Delay(5);
                using (ts.Scope(inner))
                    Thread.Sleep(1);
            }
        }
        finally
        {
            session = ts.Stop();
        }

        using var ms = new MemoryStream();
        TraceExport.WriteChromeComplete(session, ms);
        ms.Position = 0;

        using var doc = JsonDocument.Parse(ms);
        var events = doc.RootElement.GetProperty("traceEvents").EnumerateArray().ToList();

        var begins = Phase(events, "b");
        var ends = Phase(events, "e");

        Assert.HasCount(1, begins);
        Assert.HasCount(1, ends);
        Assert.AreEqual(
            begins[0].GetProperty("id").GetInt64(),
            ends[0].GetProperty("id").GetInt64());
        Assert.IsLessThanOrEqualTo(
            ends[0].GetProperty("ts").GetDouble(),
            begins[0].GetProperty("ts").GetDouble());

        var complete = Phase(events, "X");
        Assert.HasCount(1, complete);
        Assert.AreEqual(inner.ToString(), complete[0].GetProperty("name").GetString());
    }

    [TestMethod]
    public async Task WriteChromeBeginEnd_AsyncScope_UsesAsyncPhases()
    {
        const int id = 8003;

        var ts = new TracingSession();
        ts.Start(new SessionOptions { ChunkCapacity = 256 });

        TraceSession session;
        try
        {
            await using (ts.ScopeAsync(id))
                await Task.Delay(5);
        }
        finally
        {
            session = ts.Stop();
        }

        using var ms = new MemoryStream();
        TraceExport.WriteChromeBeginEnd(session, ms);
        ms.Position = 0;

        using var doc = JsonDocument.Parse(ms);
        var events = doc.RootElement.GetProperty("traceEvents").EnumerateArray().ToList();

        Assert.IsEmpty(Phase(events, "B"));
        Assert.IsEmpty(Phase(events, "E"));
        Assert.HasCount(1, Phase(events, "b"));
        Assert.HasCount(1, Phase(events, "e"));
    }

    private static List<JsonElement> Phase(List<JsonElement> events, string phase)
    {
        var list = new List<JsonElement>();
        foreach (var e in events)
        {
            if (e.TryGetProperty("ph", out var ph) && ph.GetString() == phase)
                list.Add(e);
        }

        return list;
    }
}
