using EmberTrace.Analysis.Model;
using EmberTrace.Analysis.Stats;
using EmberTrace.Internal.Time;
using EmberTrace.Sessions;

namespace EmberTrace.Analysis.Analyzers;

internal static class CallTreeBuilder
{
    public static ProcessedTrace Process(TraceSession session, bool strict, bool groupByThread)
    {
        var conv = TickConverter.FromSession(session);

        var roots = new Dictionary<int, MutableNode>(8);
        var hotspots = new Dictionary<int, HotAgg>(256);
        var reader = new ScopeReader(session, strict, session.Options.OnMismatchedEnd);

        foreach (var step in reader.Read())
        {
            if (step.Kind == ScopeStepKind.Open)
            {
                var parentNode = step.ParentTag is TreeFrame parent ? parent.Node : GetRoot(roots, step.TrackId);
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
            agg.Histogram.Add(inclusive);

            if (step.ParentTag is TreeFrame parentFrame)
                parentFrame.ChildTicks += inclusive;
        }

        foreach (var track in reader.Tracks)
            GetRoot(roots, track.Key);

        var threadList = new List<ThreadTrace>(roots.Count);
        foreach (var kv in roots)
            threadList.Add(new ThreadTrace
            {
                TrackId = kv.Key,
                ThreadId = reader.Tracks.TryGetValue(kv.Key, out var threadId) ? threadId : kv.Key,
                Root = Freeze(kv.Value, conv)
            });

        threadList.Sort((a, b) => a.TrackId.CompareTo(b.TrackId));

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
            threadList = new List<ThreadTrace>
            {
                new()
                {
                    TrackId = 0,
                    ThreadId = 0,
                    Root = globalFrozen
                }
            };

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
                ExclusiveMs = conv.ToMs(a.ExclusiveTicks),
                Durations = a.Histogram,
                P50Ms = conv.ToMs(a.Histogram.PercentileTicks(50)),
                P95Ms = conv.ToMs(a.Histogram.PercentileTicks(95)),
                P99Ms = conv.ToMs(a.Histogram.PercentileTicks(99))
            });
        }

        hotList.Sort((x, y) => y.InclusiveMs.CompareTo(x.InclusiveMs));

        return new ProcessedTrace
        {
            DurationMs = session.DurationMs,
            TotalEventCount = reader.TotalEventCount,
            ScopeEventCount = reader.ScopeEventCount,
            ThreadsSeen = reader.Tracks.Count,
            UnmatchedBeginCount = reader.UnmatchedBeginCount,
            UnmatchedEndCount = reader.UnmatchedEndCount,
            MismatchedEndCount = reader.MismatchedEndCount,
            DroppedEvents = session.DroppedEvents,
            DroppedChunks = session.DroppedChunks,
            SampledOutEvents = session.SampledOutEvents,
            WasOverflow = session.WasOverflow,
            Metadata = session.Metadata,
            Threads = threadList,
            GlobalRoot = globalFrozen,
            HotspotsByInclusiveDesc = hotList
        };
    }

    private static MutableNode GetRoot(Dictionary<int, MutableNode> roots, int trackId)
    {
        if (!roots.TryGetValue(trackId, out var root))
        {
            root = new MutableNode(0);
            roots.Add(trackId, root);
        }

        return root;
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

    private sealed class MutableNode
    {
        public readonly int Id;
        public Dictionary<int, MutableNode>? Children;
        public long Count;
        public long ExclusiveTicks;
        public long InclusiveTicks;

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
        public readonly DurationHistogram Histogram = new();
        public long Count;
        public long ExclusiveTicks;
        public long InclusiveTicks;
    }
}
