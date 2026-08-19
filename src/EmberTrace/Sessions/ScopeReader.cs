using System;
using System.Collections.Generic;

namespace EmberTrace.Sessions;

public enum ScopeStepKind : byte
{
    Open = 1,
    Close = 2
}

internal sealed class ScopeFrame
{
    public ScopeFrame(int index, ScopeFrame? parent, int id, long asyncScopeId, int threadId, long startTimestamp, long startSequence)
    {
        Index = index;
        Parent = parent;
        Depth = parent is null ? 0 : parent.Depth + 1;
        Id = id;
        AsyncScopeId = asyncScopeId;
        ThreadId = threadId;
        StartTimestamp = startTimestamp;
        StartSequence = startSequence;
    }

    public int Index { get; }
    public ScopeFrame? Parent { get; }
    public int Depth { get; }
    public int Id { get; }
    public long AsyncScopeId { get; }
    public int ThreadId { get; }
    public long StartTimestamp { get; }
    public long StartSequence { get; }
    public object? Tag { get; set; }
}

public readonly struct ScopeStep
{
    private readonly ScopeFrame _frame;

    internal ScopeStep(ScopeStepKind kind, ScopeFrame frame, int endThreadId, long endTimestamp, bool synthetic)
    {
        Kind = kind;
        _frame = frame;
        EndThreadId = endThreadId;
        EndTimestamp = endTimestamp;
        IsSynthetic = synthetic;
    }

    public ScopeStepKind Kind { get; }
    public int EndThreadId { get; }
    public long EndTimestamp { get; }
    public bool IsSynthetic { get; }

    public int Index => _frame.Index;
    public int Depth => _frame.Depth;
    public int Id => _frame.Id;
    public int ParentId => _frame.Parent?.Id ?? 0;
    public int ThreadId => _frame.ThreadId;
    public long AsyncScopeId => _frame.AsyncScopeId;
    public long StartTimestamp => _frame.StartTimestamp;
    public long StartSequence => _frame.StartSequence;

    public bool IsAsync => _frame.AsyncScopeId != 0;
    public bool HasParent => _frame.Parent is not null;
    public long DurationTicks => EndTimestamp - _frame.StartTimestamp;

    public object? Tag
    {
        get => _frame.Tag;
        set => _frame.Tag = value;
    }

    public object? ParentTag => _frame.Parent?.Tag;
}

public sealed class ScopeReader
{
    private readonly IEnumerable<TraceEventRecord> _events;
    private readonly long _endTimestamp;
    private readonly bool _strict;
    private readonly Action<MismatchedEndInfo>? _onMismatchedEnd;
    private readonly HashSet<int> _threads = new();

    public ScopeReader(TraceSession session, bool strict = false, Action<MismatchedEndInfo>? onMismatchedEnd = null)
        : this(
            Sorted(session ?? throw new ArgumentNullException(nameof(session))),
            session.EndTimestamp,
            strict,
            onMismatchedEnd)
    {
    }

    public ScopeReader(
        IEnumerable<TraceEventRecord> events,
        long endTimestamp,
        bool strict = false,
        Action<MismatchedEndInfo>? onMismatchedEnd = null)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _endTimestamp = endTimestamp;
        _strict = strict;
        _onMismatchedEnd = onMismatchedEnd;
    }

    public long TotalEvents { get; private set; }
    public long UnmatchedBeginCount { get; private set; }
    public long UnmatchedEndCount { get; private set; }
    public long MismatchedEndCount { get; private set; }
    public IReadOnlyCollection<int> Threads => _threads;

    public IEnumerable<ScopeStep> Read()
    {
        var tracks = new Dictionary<TrackKey, List<ScopeFrame>>(capacity: 8);
        var asyncFrames = new Dictionary<long, ScopeFrame>(capacity: 8);
        var index = 0;

        foreach (var e in _events)
        {
            if (!e.IsScope)
                continue;

            TotalEvents++;
            _threads.Add(e.ThreadId);

            var contextId = e.AsyncContextId;
            ScopeFrame? context = null;

            if (contextId != 0 && !asyncFrames.TryGetValue(contextId, out context))
                contextId = 0;

            var key = new TrackKey(e.ThreadId, contextId);
            if (!tracks.TryGetValue(key, out var track))
            {
                track = new List<ScopeFrame>(capacity: 64);
                tracks.Add(key, track);
            }

            if (e.Kind == TraceEventKind.Begin)
            {
                var parent = track.Count > 0 ? track[^1] : context;

                var frame = new ScopeFrame(index++, parent, e.Id, e.AsyncScopeId, e.ThreadId, e.Timestamp, e.Sequence);

                if (frame.AsyncScopeId != 0)
                    asyncFrames[frame.AsyncScopeId] = frame;
                else
                    track.Add(frame);

                yield return new ScopeStep(ScopeStepKind.Open, frame, 0, 0, synthetic: false);
                continue;
            }

            if (e.AsyncScopeId != 0)
            {
                if (!asyncFrames.Remove(e.AsyncScopeId, out var asyncFrame))
                {
                    UnmatchedEndCount++;
                    continue;
                }

                yield return new ScopeStep(ScopeStepKind.Close, asyncFrame, e.ThreadId, e.Timestamp, synthetic: false);
                continue;
            }

            if (track.Count == 0)
            {
                UnmatchedEndCount++;
                continue;
            }

            var top = track[^1];
            if (top.Id != e.Id)
            {
                MismatchedEndCount++;
                _onMismatchedEnd?.Invoke(new MismatchedEndInfo(e.ThreadId, top.Id, e.Id, e.Timestamp));

                if (_strict)
                    continue;

                var target = -1;
                for (int i = track.Count - 2; i >= 0; i--)
                {
                    if (track[i].Id == e.Id)
                    {
                        target = i;
                        break;
                    }
                }

                if (target < 0)
                {
                    UnmatchedEndCount++;
                    continue;
                }

                for (int i = track.Count - 1; i > target; i--)
                {
                    UnmatchedBeginCount++;
                    yield return new ScopeStep(ScopeStepKind.Close, track[i], e.ThreadId, e.Timestamp, synthetic: true);
                }

                track.RemoveRange(target + 1, track.Count - (target + 1));
                top = track[^1];
            }

            track.RemoveAt(track.Count - 1);
            yield return new ScopeStep(ScopeStepKind.Close, top, e.ThreadId, e.Timestamp, synthetic: false);
        }

        foreach (var kv in tracks)
        {
            var track = kv.Value;
            for (int i = track.Count - 1; i >= 0; i--)
            {
                UnmatchedBeginCount++;
                yield return new ScopeStep(ScopeStepKind.Close, track[i], track[i].ThreadId, _endTimestamp, synthetic: true);
            }
        }

        foreach (var kv in asyncFrames)
        {
            UnmatchedBeginCount++;
            yield return new ScopeStep(ScopeStepKind.Close, kv.Value, kv.Value.ThreadId, _endTimestamp, synthetic: true);
        }
    }

    private static IEnumerable<TraceEventRecord> Sorted(TraceSession session)
    {
        foreach (var e in session.EnumerateEventsSorted())
            yield return e;
    }

    private readonly record struct TrackKey(int ThreadId, long ContextId);
}
