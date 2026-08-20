using System.Collections.Generic;
using EmberTrace.Sessions;

namespace EmberTrace.Export;

internal readonly struct CompleteSpan
{
    public readonly int Id;
    public readonly int TrackId;
    public readonly long StartTs;
    public readonly long DurTicks;
    public readonly int Depth;
    public readonly int ParentId;
    public readonly long Sequence;

    public CompleteSpan(int id, int trackId, long startTs, long durTicks, int depth, int parentId, long sequence)
    {
        Id = id;
        TrackId = trackId;
        StartTs = startTs;
        DurTicks = durTicks;
        Depth = depth;
        ParentId = parentId;
        Sequence = sequence;
    }
}

internal readonly struct AsyncSpan
{
    public readonly int Id;
    public readonly long AsyncScopeId;
    public readonly int StartTrackId;
    public readonly int EndTrackId;
    public readonly long StartTs;
    public readonly long EndTs;
    public readonly long Sequence;

    public AsyncSpan(int id, long asyncScopeId, int startTrackId, int endTrackId, long startTs, long endTs, long sequence)
    {
        Id = id;
        AsyncScopeId = asyncScopeId;
        StartTrackId = startTrackId;
        EndTrackId = endTrackId;
        StartTs = startTs;
        EndTs = endTs;
        Sequence = sequence;
    }
}

internal static class ScopeCollector
{
    public static void CollectComplete(
        ScopeReader reader,
        long minStartTimestamp,
        List<CompleteSpan> complete,
        List<AsyncSpan> asyncSpans)
    {
        foreach (var step in reader.Read())
        {
            if (step.Kind != ScopeStepKind.Close || step.IsSynthetic)
                continue;

            if (step.StartTimestamp < minStartTimestamp)
                continue;

            var dur = step.DurationTicks;
            if (dur < 0)
                continue;

            if (step.IsAsync)
            {
                asyncSpans.Add(new AsyncSpan(
                    step.Id,
                    step.AsyncScopeId,
                    step.TrackId,
                    step.EndTrackId,
                    step.StartTimestamp,
                    step.EndTimestamp,
                    step.StartSequence));
                continue;
            }

            complete.Add(new CompleteSpan(
                step.Id,
                step.TrackId,
                step.StartTimestamp,
                dur,
                step.Depth,
                step.ParentId,
                step.StartSequence));
        }
    }
}
