using System;
using System.Collections.Generic;
using EmberTrace.Internal.Buffering;
using EmberTrace.Internal.Time;
using EmberTrace.Processing.Model;
using EmberTrace.Public;
using EmberTrace.Reporting;

namespace EmberTrace.Processing;

internal static class TraceProcessor
{
    private sealed class Frame
    {
        public int Id;
        public long Start;
    }

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

    private readonly struct TreeFrame
    {
        public readonly int Id;
        public readonly long Start;
        public readonly MutableNode Node;
        public readonly long ChildTicks;

        public TreeFrame(int id, long start, MutableNode node, long childTicks)
        {
            Id = id;
            Start = start;
            Node = node;
            ChildTicks = childTicks;
        }

        public TreeFrame WithChildTicks(long childTicks) => new(Id, Start, Node, childTicks);
    }

    private sealed class HotAgg
    {
        public long Count;
        public long InclusiveTicks;
        public long ExclusiveTicks;
    }

    public static TraceStats Analyze(TraceSession session)
    {
        var freq = Timestamp.Frequency;
        var perThread = new Dictionary<int, List<Frame>>(capacity: 8);
        var perId = new Dictionary<int, Agg>(capacity: 256);

        long totalEvents = 0;
        long mismatchedEnd = 0;

        var chunks = session.Chunks;
        for (int ci = 0; ci < chunks.Count; ci++)
        {
            var c = chunks[ci];
            var events = c.Events;
            for (int i = 0; i < c.Count; i++)
            {
                var e = events[i];
                totalEvents++;

                if (!perThread.TryGetValue(e.ThreadId, out var stack))
                {
                    stack = new List<Frame>(capacity: 64);
                    perThread.Add(e.ThreadId, stack);
                }

                if (e.Kind == TraceEventKind.Begin)
                {
                    stack.Add(new Frame { Id = e.Id, Start = e.Timestamp });
                    continue;
                }

                if (stack.Count == 0)
                {
                    mismatchedEnd++;
                    continue;
                }

                var top = stack[^1];
                if (top.Id != e.Id)
                {
                    var idx = -1;
                    for (int s = stack.Count - 2; s >= 0; s--)
                    {
                        if (stack[s].Id == e.Id)
                        {
                            idx = s;
                            break;
                        }
                    }

                    if (idx < 0)
                    {
                        mismatchedEnd++;
                        continue;
                    }

                    stack.RemoveRange(idx + 1, stack.Count - (idx + 1));
                    top = stack[^1];
                }

                stack.RemoveAt(stack.Count - 1);

                var dtTicks = e.Timestamp - top.Start;
                if (dtTicks < 0)
                    continue;

                var ms = dtTicks * 1000.0 / freq;

                if (!perId.TryGetValue(e.Id, out var agg))
                {
                    agg = new Agg();
                    perId.Add(e.Id, agg);
                }

                agg.Add(ms);
            }
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
            TotalEvents = totalEvents,
            ThreadsSeen = perThread.Count,
            MismatchedEndCount = mismatchedEnd,
            ByTotalTimeDesc = list
        };
    }

    public static ProcessedTrace Process(TraceSession session)
    {
        var conv = new TickConverter(Timestamp.Frequency);

        var roots = new Dictionary<int, MutableNode>(capacity: 8);
        var stacks = new Dictionary<int, List<TreeFrame>>(capacity: 8);
        var hotspots = new Dictionary<int, HotAgg>(capacity: 256);

        long totalEvents = 0;
        long mismatchedEnd = 0;

        var chunks = session.Chunks;
        for (int ci = 0; ci < chunks.Count; ci++)
        {
            var c = chunks[ci];
            var events = c.Events;

            for (int i = 0; i < c.Count; i++)
            {
                var e = events[i];
                totalEvents++;

                if (!roots.TryGetValue(e.ThreadId, out var root))
                {
                    root = new MutableNode(0);
                    roots.Add(e.ThreadId, root);
                }

                if (!stacks.TryGetValue(e.ThreadId, out var stack))
                {
                    stack = new List<TreeFrame>(capacity: 64);
                    stacks.Add(e.ThreadId, stack);
                }

                if (e.Kind == TraceEventKind.Begin)
                {
                    var parent = stack.Count == 0 ? root : stack[^1].Node;
                    var node = parent.GetOrAddChild(e.Id);
                    node.Count++;

                    stack.Add(new TreeFrame(e.Id, e.Timestamp, node, 0));
                    continue;
                }

                if (stack.Count == 0)
                {
                    mismatchedEnd++;
                    continue;
                }

                var top = stack[^1];
                if (top.Id != e.Id)
                {
                    var idx = -1;
                    for (int s = stack.Count - 2; s >= 0; s--)
                    {
                        if (stack[s].Id == e.Id)
                        {
                            idx = s;
                            break;
                        }
                    }

                    if (idx < 0)
                    {
                        mismatchedEnd++;
                        continue;
                    }

                    stack.RemoveRange(idx + 1, stack.Count - (idx + 1));
                    top = stack[^1];
                }

                stack.RemoveAt(stack.Count - 1);

                var inclusive = e.Timestamp - top.Start;
                if (inclusive < 0)
                    continue;

                var exclusive = inclusive - top.ChildTicks;
                if (exclusive < 0)
                    exclusive = 0;

                top.Node.InclusiveTicks += inclusive;
                top.Node.ExclusiveTicks += exclusive;

                if (!hotspots.TryGetValue(e.Id, out var agg))
                {
                    agg = new HotAgg();
                    hotspots.Add(e.Id, agg);
                }

                agg.Count++;
                agg.InclusiveTicks += inclusive;
                agg.ExclusiveTicks += exclusive;

                if (stack.Count > 0)
                {
                    var parentIndex = stack.Count - 1;
                    var parent = stack[parentIndex];
                    stack[parentIndex] = parent.WithChildTicks(parent.ChildTicks + inclusive);
                }
            }
        }

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
            TotalEvents = totalEvents,
            ThreadsSeen = roots.Count,
            MismatchedEndCount = mismatchedEnd,
            Threads = threadList,
            HotspotsByInclusiveDesc = hotList
        };
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

}
