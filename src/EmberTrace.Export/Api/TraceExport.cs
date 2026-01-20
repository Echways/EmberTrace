using System.IO;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace;

public static class TraceExport
{
    public static void WriteChromeComplete(
        TraceSession session,
        Stream output,
        ITraceMetadataProvider? meta = null,
        bool sortByStartTimestamp = true,
        int pid = 1,
        string processName = "EmberTrace")
    {
        Export.ChromeTraceExporter.WriteComplete(session, output, meta, sortByStartTimestamp, pid, processName);
    }

    public static void WriteChromeBeginEnd(
        TraceSession session,
        Stream output,
        ITraceMetadataProvider? meta = null,
        bool sortByTimestamp = true,
        int pid = 1,
        string processName = "EmberTrace")
    {
        Export.ChromeTraceExporter.WriteBeginEnd(session, output, meta, sortByTimestamp, pid, processName);
    }
}
