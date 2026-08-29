using EmberTrace.Analysis.Model;
using EmberTrace.Analysis.Stats;
using EmberTrace.Internal.Time;
using EmberTrace.Sessions;

namespace EmberTrace.Analysis.Analyzers;

internal static class FlowAnalyzer
{
    public static IReadOnlyList<FlowAnalysis> AnalyzeFlows(TraceSession session, int top)
    {
        var conv = TickConverter.FromSession(session);
        var flows = new Dictionary<long, List<FlowEvent>>(16);

        foreach (var e in session.EnumerateEventsSorted())
        {
            if (e.FlowId == 0)
                continue;

            if (e.Kind != TraceEventKind.FlowStart
                && e.Kind != TraceEventKind.FlowStep
                && e.Kind != TraceEventKind.FlowEnd)
                continue;

            if (!flows.TryGetValue(e.FlowId, out var list))
            {
                list = new List<FlowEvent>();
                flows.Add(e.FlowId, list);
            }

            list.Add(new FlowEvent(e.Id, e.Kind, e.Timestamp));
        }

        var results = new List<FlowAnalysis>(flows.Count);

        foreach (var kv in flows)
        {
            var flowId = kv.Key;
            var list = kv.Value;
            if (list.Count < 2)
                continue;

            var startIndex = list.FindIndex(static x => x.Kind == TraceEventKind.FlowStart);
            if (startIndex < 0)
                continue;

            var endIndex = list.FindLastIndex(static x => x.Kind == TraceEventKind.FlowEnd);
            if (endIndex <= startIndex)
                continue;

            var start = list[startIndex];
            var end = list[endIndex];
            if (end.Timestamp < start.Timestamp)
                continue;

            var steps = new List<FlowStepInfo>(endIndex - startIndex);
            for (var i = startIndex; i < endIndex; i++)
            {
                var current = list[i];
                var next = list[i + 1];
                var dt = next.Timestamp - current.Timestamp;
                if (dt < 0)
                    dt = 0;

                steps.Add(new FlowStepInfo
                {
                    Id = current.Id,
                    Kind = current.Kind,
                    Timestamp = current.Timestamp,
                    DurationMs = conv.ToMs(dt)
                });
            }

            var totalMs = conv.ToMs(end.Timestamp - start.Timestamp);

            results.Add(new FlowAnalysis
            {
                FlowId = flowId,
                Id = start.Id,
                StartTimestamp = start.Timestamp,
                EndTimestamp = end.Timestamp,
                TotalDurationMs = totalMs,
                Steps = steps
            });
        }

        results.Sort((a, b) => b.TotalDurationMs.CompareTo(a.TotalDurationMs));

        if (top > 0 && results.Count > top)
            results = results.GetRange(0, top);

        return results;
    }

    private readonly struct FlowEvent
    {
        public readonly int Id;
        public readonly TraceEventKind Kind;
        public readonly long Timestamp;

        public FlowEvent(int id, TraceEventKind kind, long timestamp)
        {
            Id = id;
            Kind = kind;
            Timestamp = timestamp;
        }
    }
}
