using System;
using System.Collections.Generic;
using EmberTrace.Sessions;
using EmberTrace.Analysis.Model;
using EmberTrace.Analysis.Stats;

namespace EmberTrace;

public static class TraceSessionExtensions
{
    private sealed class Agg
    {
        public long Count;
        public double TotalMs;
        public double MinMs = double.PositiveInfinity;
        public double MaxMs = 0;

        public void Add(double ms)
        {
            Count++;
            TotalMs += ms;
            if (ms < MinMs) MinMs = ms;
            if (ms > MaxMs) MaxMs = ms;
        }
    }

    private sealed class MutableNode
    {
        public int Id;
        public long Count;
        public long InclusiveTicks;
        public long ExclusiveTicks;
        public Dictionary<int, MutableNode>? Children;

        public MutableNode(int id)
        {
            Id = id;
        }

        public MutableNode GetOrAddChild(int id)
        {
            var dict = Children ??= new Dictionary<int, MutableNode>();
            if (!dict.TryGetValue(id, out var n))
            {
                n = new MutableNode(id);
                dict.Add(id, n);
            }
            return n;
        }
    }

    private sealed class TreeFrame
    {
        public TreeFrame(MutableNode node)
        {
            Node = node;
        }

        public MutableNode Node { get; }
        public long ChildTicks { get; set; }
    }

    private sealed class HotAgg
    {
        public long Count;
        public long InclusiveTicks;
        public long ExclusiveTicks;
    }

    private readonly struct TickConverter
    {
        private readonly long _frequency;

        public TickConverter(long frequency)
        {
            _frequency = frequency;
        }

        public double ToMs(long ticks) => ticks * 1000.0 / _frequency;
    }

    public static TraceStats Analyze(this TraceSession session, bool strict = false)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));

        var freq = session.TimestampFrequency;
        var perId = new Dictionary<int, Agg>(capacity: 256);
        var reader = new ScopeReader(session, strict, session.Options.OnMismatchedEnd);

        foreach (var step in reader.Read())
        {
            if (step.Kind != ScopeStepKind.Close || step.IsSynthetic)
                continue;

            var dtTicks = step.DurationTicks;
            if (dtTicks < 0)
                continue;

            if (!perId.TryGetValue(step.Id, out var agg))
            {
                agg = new Agg();
                perId.Add(step.Id, agg);
            }

            agg.Add(dtTicks * 1000.0 / freq);
        }

        var list = new List<TraceIdStats>(perId.Count);
        foreach (var kv in perId)
        {
            var id = kv.Key;
            var a = kv.Value;
            var min = double.IsPositiveInfinity(a.MinMs) ? 0 : a.MinMs;

            list.Add(new TraceIdStats
            {
                Id = id,
                Count = a.Count,
                TotalMs = a.TotalMs,
                AverageMs = a.Count == 0 ? 0 : a.TotalMs / a.Count,
                MinMs = min,
                MaxMs = a.MaxMs
            });
        }

        list.Sort((x, y) => y.TotalMs.CompareTo(x.TotalMs));

        return new TraceStats
        {
            DurationMs = session.DurationMs,
            TotalEvents = reader.TotalEvents,
            ThreadsSeen = reader.Threads.Count,
            UnmatchedBeginCount = reader.UnmatchedBeginCount,
            UnmatchedEndCount = reader.UnmatchedEndCount,
            MismatchedEndCount = reader.MismatchedEndCount,
            ByTotalTimeDesc = list
        };
    }

    public static ProcessedTrace Process(this TraceSession session, bool strict = false, bool groupByThread = true)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));

        var conv = new TickConverter(session.TimestampFrequency);

        var roots = new Dictionary<int, MutableNode>(capacity: 8);
        var hotspots = new Dictionary<int, HotAgg>(capacity: 256);
        var reader = new ScopeReader(session, strict, session.Options.OnMismatchedEnd);

        foreach (var step in reader.Read())
        {
            if (step.Kind == ScopeStepKind.Open)
            {
                var parentNode = step.ParentTag is TreeFrame parent ? parent.Node : GetRoot(roots, step.ThreadId);
                var node = parentNode.GetOrAddChild(step.Id);
                node.Count++;
                step.Tag = new TreeFrame(node);
                continue;
            }

            if (step.IsSynthetic || step.Tag is not TreeFrame frame)
                continue;

            var inclusive = step.DurationTicks;
            if (inclusive < 0)
                continue;

            var exclusive = inclusive - frame.ChildTicks;
            if (exclusive < 0)
                exclusive = 0;

            frame.Node.InclusiveTicks += inclusive;
            frame.Node.ExclusiveTicks += exclusive;

            if (!hotspots.TryGetValue(step.Id, out var agg))
            {
                agg = new HotAgg();
                hotspots.Add(step.Id, agg);
            }

            agg.Count++;
            agg.InclusiveTicks += inclusive;
            agg.ExclusiveTicks += exclusive;

            if (step.ParentTag is TreeFrame parentFrame)
                parentFrame.ChildTicks += inclusive;
        }

        foreach (var threadId in reader.Threads)
            GetRoot(roots, threadId);

        var threadList = new List<ThreadTrace>(roots.Count);
        foreach (var kv in roots)
        {
            threadList.Add(new ThreadTrace
            {
                ThreadId = kv.Key,
                Root = Freeze(kv.Value, conv)
            });
        }

        threadList.Sort((a, b) => a.ThreadId.CompareTo(b.ThreadId));

        var globalRoot = new MutableNode(0);
        foreach (var kv in roots)
        {
            var root = kv.Value;
            if (root.Children is null)
                continue;

            foreach (var child in root.Children.Values)
            {
                var target = globalRoot.GetOrAddChild(child.Id);
                MergeInto(target, child);
            }
        }

        var globalFrozen = Freeze(globalRoot, conv);

        if (!groupByThread)
        {
            threadList = new List<ThreadTrace>
            {
                new ThreadTrace
                {
                    ThreadId = 0,
                    Root = globalFrozen
                }
            };
        }

        var hotList = new List<HotspotRow>(hotspots.Count);
        foreach (var kv in hotspots)
        {
            var id = kv.Key;
            var a = kv.Value;
            hotList.Add(new HotspotRow
            {
                Id = id,
                Count = a.Count,
                InclusiveMs = conv.ToMs(a.InclusiveTicks),
                ExclusiveMs = conv.ToMs(a.ExclusiveTicks)
            });
        }

        hotList.Sort((x, y) => y.InclusiveMs.CompareTo(x.InclusiveMs));

        return new ProcessedTrace
        {
            DurationMs = session.DurationMs,
            TotalEvents = reader.TotalEvents,
            ThreadsSeen = reader.Threads.Count,
            UnmatchedBeginCount = reader.UnmatchedBeginCount,
            UnmatchedEndCount = reader.UnmatchedEndCount,
            MismatchedEndCount = reader.MismatchedEndCount,
            DroppedEvents = session.DroppedEvents,
            DroppedChunks = session.DroppedChunks,
            SampledOutEvents = session.SampledOutEvents,
            WasOverflow = session.WasOverflow,
            Threads = threadList,
            GlobalRoot = globalFrozen,
            HotspotsByInclusiveDesc = hotList
        };
    }

    private static MutableNode GetRoot(Dictionary<int, MutableNode> roots, int threadId)
    {
        if (!roots.TryGetValue(threadId, out var root))
        {
            root = new MutableNode(0);
            roots.Add(threadId, root);
        }

        return root;
    }

    public static IReadOnlyList<FlowAnalysis> AnalyzeFlows(this TraceSession session, int top = 10)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));

        var freq = session.TimestampFrequency;
        var flows = new Dictionary<long, List<FlowEvent>>(capacity: 16);

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
            for (int i = startIndex; i < endIndex; i++)
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
                    DurationMs = dt * 1000.0 / freq
                });
            }

            var totalMs = (end.Timestamp - start.Timestamp) * 1000.0 / freq;

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

    private static CallTreeNode Freeze(MutableNode n, TickConverter conv)
    {
        CallTreeNode[]? children = null;

        if (n.Children is not null && n.Children.Count > 0)
        {
            var list = new List<CallTreeNode>(n.Children.Count);
            foreach (var kv in n.Children)
                list.Add(Freeze(kv.Value, conv));

            list.Sort((a, b) => b.InclusiveMs.CompareTo(a.InclusiveMs));
            children = list.ToArray();
        }

        return new CallTreeNode
        {
            Id = n.Id,
            Count = n.Count,
            InclusiveMs = conv.ToMs(n.InclusiveTicks),
            ExclusiveMs = conv.ToMs(n.ExclusiveTicks),
            Children = children ?? Array.Empty<CallTreeNode>()
        };
    }

    private static void MergeInto(MutableNode target, MutableNode source)
    {
        target.Count += source.Count;
        target.InclusiveTicks += source.InclusiveTicks;
        target.ExclusiveTicks += source.ExclusiveTicks;

        if (source.Children is null)
            return;

        foreach (var kv in source.Children)
        {
            var child = kv.Value;
            var targetChild = target.GetOrAddChild(child.Id);
            MergeInto(targetChild, child);
        }
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
