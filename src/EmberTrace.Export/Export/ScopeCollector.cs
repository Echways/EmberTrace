using System.Collections.Generic;
using EmberTrace.Sessions;

namespace EmberTrace.Export;

internal readonly struct CompleteSpan
{
    public readonly int Id;
    public readonly int ThreadId;
    public readonly long StartTs;
    public readonly long DurTicks;
    public readonly int Depth;
    public readonly int ParentId;
    public readonly long Sequence;

    public CompleteSpan(int id, int threadId, long startTs, long durTicks, int depth, int parentId, long sequence)
    {
        Id = id;
        ThreadId = threadId;
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
    public readonly int StartThreadId;
    public readonly int EndThreadId;
    public readonly long StartTs;
    public readonly long EndTs;
    public readonly long Sequence;

    public AsyncSpan(int id, long asyncScopeId, int startThreadId, int endThreadId, long startTs, long endTs, long sequence)
    {
        Id = id;
        AsyncScopeId = asyncScopeId;
        StartThreadId = startThreadId;
        EndThreadId = endThreadId;
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
                    step.ThreadId,
                    step.EndThreadId,
                    step.StartTimestamp,
                    step.EndTimestamp,
                    step.StartSequence));
                continue;
            }

            complete.Add(new CompleteSpan(
                step.Id,
                step.ThreadId,
                step.StartTimestamp,
                dur,
                step.Depth,
                step.ParentId,
                step.StartSequence));
        }
    }
}
